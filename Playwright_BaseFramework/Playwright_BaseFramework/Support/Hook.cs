using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Playwright;
using Reqnroll;

namespace Playwright_BaseFramework.Support
{
    [Binding]
    public sealed class Hook
    {
        public PageObject pageObject = null!;
        public static IPlaywright? playwright;
        public static IBrowser? browser;
        public IBrowserContext browserContext = null!;
        private ScenarioContext scenarioContext = null!;
        private FeatureContext featureContext = null!;
        private static string browserType = "Chrome";
        private static List<ScenarioReport> scenarioReports = new List<ScenarioReport>();
        private static readonly object scenarioReportsLock = new object();
        private string traceFilePath = string.Empty;
        private string screenshotPath = string.Empty;
        private static DateTime testRunStartTime;
        private static DateTime testRunEndTime;
        private List<StepExecution> scenarioSteps = new List<StepExecution>();

        // Base path for all test artifacts
        public static string BasePath => AppDomain.CurrentDomain.BaseDirectory;
        public static string BaseUrl => Environment.GetEnvironmentVariable("PLAYWRIGHT_BaseUrl") ?? string.Empty;
        private static string TracesDirectory => Path.Combine(BasePath, "PlaywrightTraces");
        private static string ScreenshotsDirectory => Path.Combine(BasePath, "PlaywrightScreenshots");
        private static string ReportPath => Path.Combine(BasePath, "PlaywrightReport.html");

        // Template locations. Both files ship alongside the test assembly (copy-to-output).
        // Prefers a "Templates" subfolder if one exists in the output directory, but falls
        // back to the output root — matching setups where the .csproj copies the files
        // directly next to the built DLL instead of into a Templates\ subfolder.
        private static string TemplatesDirectory
        {
            get
            {
                var withSubfolder = Path.Combine(BasePath, "Templates");
                return Directory.Exists(withSubfolder) ? withSubfolder : BasePath;
            }
        }
        private static string HtmlTemplatePath => Path.Combine(TemplatesDirectory, "ReportTemplate.html");
        private static string CssTemplatePath => Path.Combine(TemplatesDirectory, "ReportTemplate.css");

        public Hook(PageObject pageObject, ScenarioContext scenarioContext, FeatureContext featureContext)
        {
            this.pageObject = pageObject;
            this.scenarioContext = scenarioContext;
            this.featureContext = featureContext;
        }

        [BeforeTestRun]
        public static async Task BeforeTestRun()
        {
            testRunStartTime = DateTime.Now;

            // Create directories if they don't exist
            Directory.CreateDirectory(TracesDirectory);
            Directory.CreateDirectory(ScreenshotsDirectory);

            playwright = await Playwright.CreateAsync();
            bool headless = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Headless") ?? "true", out var h) ? h : true;
            int slowMo = int.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_SlowMo") ?? "1000", out var s) ? s : 1000;
            var browserLaunchOptions = new BrowserTypeLaunchOptions()
            {
                Headless = headless,
                SlowMo = slowMo
            };

            browserType = Environment.GetEnvironmentVariable("PLAYWRIGHT_BrowserType") ?? "Chrome";
            switch (browserType)
            {
                case "Chrome":
                case "Edge":
                    browser = await playwright.Chromium.LaunchAsync(browserLaunchOptions);
                    break;
                case "Firefox":
                    browser = await playwright.Firefox.LaunchAsync(browserLaunchOptions);
                    break;
                case "Safari":
                    browser = await playwright.Webkit.LaunchAsync(browserLaunchOptions);
                    break;
                default:
                    throw new Exception("Please provide correct browser type in runsettings. Browser type can be Chrome, Edge, Firefox, Safari");
            }

            var loginBrowserContext = await browser.NewContextAsync();
            var loginPage = await loginBrowserContext.NewPageAsync();
            await loginPage.GotoAsync(BaseUrl, new()
            {
                Timeout = 40_000
            });

            string username = Environment.GetEnvironmentVariable("PLAYWRIGHT_Username") ?? string.Empty;
            string password = Environment.GetEnvironmentVariable("PLAYWRIGHT_Password") ?? string.Empty;

            await loginPage.Locator("[data-test=\"username\"]").ClickAsync();
            await loginPage.Locator("[data-test=\"username\"]").FillAsync(username);
            await loginPage.Locator("[data-test=\"password\"]").ClickAsync();
            await loginPage.Locator("[data-test=\"password\"]").FillAsync(password);
            await loginPage.Locator("[data-test=\"login-button\"]").ClickAsync(new()
            {
                Delay = 5_000
            });
            await loginBrowserContext.StorageStateAsync(new()
            {
                Path = "auth.json" //localstorage & cookies
            });
        }

        [BeforeScenario]
        public async Task BeforeScenario()
        {
            this.scenarioSteps.Clear();
            CaptureScenarioStepsFromFeature();

            bool screenshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Screenshots") ?? "true", out var ss) ? ss : true;
            bool snapshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Snapshots") ?? "true", out var sp) ? sp : true;

                this.browserContext = await (browser ?? throw new InvalidOperationException("Browser instance is not initialized.")).NewContextAsync(new BrowserNewContextOptions()
            {
               StorageStatePath = "auth.json"
            });

            await this.browserContext.Tracing.StartAsync(new()
            {
                Screenshots = screenshots,
                Snapshots = snapshots
            });
            this.pageObject.Page = await this.browserContext.NewPageAsync();
        }

        [AfterStep]
        public async Task AfterStep()
        {
            try
            {
                var stepInfo = this.scenarioContext.StepContext?.StepInfo;
                if (stepInfo != null)
                {
                    var stepExecution = new StepExecution
                    {
                        StepDefinitionType = stepInfo.StepDefinitionType.ToString(),
                        Text = stepInfo.Text,
                        Status = this.scenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.OK ? "Passed" : "Failed",
                        ErrorMessage = this.scenarioContext.TestError?.Message ?? string.Empty
                    };

                    this.scenarioSteps.Add(stepExecution);

                    // Capture screenshot on step failure, before context might close
                    if (this.scenarioContext.ScenarioExecutionStatus != ScenarioExecutionStatus.OK && this.scenarioContext.TestError != null)
                    {
                        try
                        {
                            if (this.pageObject?.Page != null && !this.pageObject.Page.IsClosed)
                            {
                                string stepScreenshotFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_step_{SanitizeFileName(stepInfo.Text)}.png";
                                string stepScreenshotPath = Path.Combine(ScreenshotsDirectory, stepScreenshotFileName);
                                await this.pageObject.Page.ScreenshotAsync(new PageScreenshotOptions { Path = stepScreenshotPath });
                                if (string.IsNullOrEmpty(this.screenshotPath))
                                {
                                    this.screenshotPath = stepScreenshotPath;
                                }
                            }
                        }
                        catch (Exception screenshotEx)
                        {
                            // Screenshot might not be possible if the page is already closed; not fatal.
                            TestContext.WriteLine($"[Hook.AfterStep] Step screenshot failed: {screenshotEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // AfterStep failures shouldn't break the test flow, but should still be visible.
                TestContext.WriteLine($"[Hook.AfterStep] Failed to record step result: {ex.Message}");
            }
        }

        [AfterScenario]
        public async Task AfterScenario()
        {
            var scenarioTitle = this.scenarioContext?.ScenarioInfo?.Title ?? "UnknownScenario";
            var traceFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_{SanitizeFileName(scenarioTitle)}_{this.scenarioContext?.ScenarioExecutionStatus}.zip";
            this.traceFilePath = Path.Combine(TracesDirectory, traceFileName);

            try
            {
                if (this.scenarioContext?.ScenarioExecutionStatus != ScenarioExecutionStatus.OK)
                {
                    await TakeScreenshot();
                }
            }
            catch (Exception ex)
            {
                this.screenshotPath = string.Empty;
                this.scenarioSteps.Add(new StepExecution
                {
                    StepDefinitionType = "Hook",
                    Text = "AfterScenario screenshot",
                    Status = "Failed",
                    ErrorMessage = $"Screenshot capture failed: {ex.Message}"
                });
            }

            try
            {
                if (this.browserContext != null)
                {
                    await this.browserContext.Tracing.StopAsync(new()
                    {
                        Path = this.traceFilePath
                    });
                }
            }
            catch (Exception ex)
            {
                this.traceFilePath = string.Empty;
                this.scenarioSteps.Add(new StepExecution
                {
                    StepDefinitionType = "Hook",
                    Text = "Tracing stop",
                    Status = "Failed",
                    ErrorMessage = $"Trace stop failed: {ex.Message}"
                });
            }

            try
            {
                RecordScenarioReport(this.traceFilePath);
            }
            catch (Exception ex)
            {
                lock (scenarioReportsLock)
                {
                    scenarioReports.Add(new ScenarioReport
                    {
                        ScenarioName = scenarioTitle,
                        FeatureName = GetFeatureNameFromContext(),
                        Status = GetScenarioStatus(),
                        ExecutionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        BrowserType = browserType,
                        TraceFilePath = this.traceFilePath,
                        ErrorMessage = string.IsNullOrEmpty(GetErrorMessage()) ? ex.Message : $"{GetErrorMessage()} | Report failure: {ex.Message}",
                        ScreenshotPath = this.screenshotPath,
                        Steps = this.scenarioSteps
                    });
                }
            }

            try
            {
                if (this.browserContext != null)
                {
                    await this.browserContext.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                // Ignore disposal failures during teardown, but keep them visible.
                TestContext.WriteLine($"[Hook.AfterScenario] BrowserContext disposal failed: {ex.Message}");
            }
        }

        [AfterTestRun]
        public static async Task AfterTestRun()
        {
            testRunEndTime = DateTime.Now;

            if (browser != null)
            {
                await browser.DisposeAsync();
            }
            playwright?.Dispose();

            // Generate HTML report
            GenerateHtmlReport();
        }

        /// <summary>
        /// Sanitizes filename by removing invalid characters
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        /// <summary>
        /// Captures Gherkin steps from the feature file
        /// </summary>
        private void CaptureScenarioStepsFromFeature()
        {
            try
            {
                if (this.scenarioContext?.ScenarioInfo == null)
                {
                    return;
                }

                var featureFilePath = GetFeatureFilePath();
                if (!string.IsNullOrEmpty(featureFilePath) && File.Exists(featureFilePath))
                {
                    var lines = File.ReadAllLines(featureFilePath);
                    var scenarioTitle = this.scenarioContext.ScenarioInfo.Title;
                    bool inScenario = false;

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();

                        if (trimmedLine.StartsWith("Scenario:") && trimmedLine.Contains(scenarioTitle))
                        {
                            inScenario = true;
                            continue;
                        }

                        if (inScenario && (trimmedLine.StartsWith("Scenario:") || trimmedLine.StartsWith("Feature:")))
                        {
                            break;
                        }

                        if (inScenario && !string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            if (trimmedLine.StartsWith("Given ") ||
                                trimmedLine.StartsWith("When ") ||
                                trimmedLine.StartsWith("Then ") ||
                                trimmedLine.StartsWith("And ") ||
                                trimmedLine.StartsWith("But "))
                            {
                                if (!this.scenarioSteps.Any(s => trimmedLine.Contains(s.Text)))
                                {
                                    var stepExecution = new StepExecution
                                    {
                                        StepDefinitionType = GetStepKeyword(trimmedLine),
                                        Text = GetStepText(trimmedLine),
                                        Status = "Pending",
                                        ErrorMessage = string.Empty
                                    };
                                    this.scenarioSteps.Add(stepExecution);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        /// <summary>
        /// Gets the feature file path
        /// </summary>
        private string GetFeatureFilePath()
        {
            try
            {
                if (this.featureContext?.FeatureInfo != null)
                {
                    var featureInfo = this.featureContext.FeatureInfo;
                    var featureInfoType = featureInfo.GetType();

                    var filePathProperty = featureInfoType.GetProperty("FeatureFilePath");
                    if (filePathProperty != null)
                    {
                        var filePath = filePathProperty.GetValue(featureInfo)?.ToString();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            return filePath;
                        }
                    }

                    var featureTitle = featureInfo.Title;
                    var projectDir = AppDomain.CurrentDomain.BaseDirectory;
                    var possiblePaths = new[]
                    {
                        Path.Combine(projectDir, "Features", $"{featureTitle}.feature"),
                        Path.Combine(projectDir, "..", "..", "..", "Features", $"{featureTitle}.feature"),
                        Path.Combine(projectDir, "..", "..", "Features", $"{featureTitle}.feature")
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

            return string.Empty;
        }

        private string GetStepKeyword(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Given ")) return "Given";
            if (trimmed.StartsWith("When ")) return "When";
            if (trimmed.StartsWith("Then ")) return "Then";
            if (trimmed.StartsWith("And ")) return "And";
            if (trimmed.StartsWith("But ")) return "But";
            return "Unknown";
        }

        private string GetStepText(string line)
        {
            var trimmed = line.Trim();
            var keywords = new[] { "Given ", "When ", "Then ", "And ", "But " };

            foreach (var keyword in keywords)
            {
                if (trimmed.StartsWith(keyword))
                {
                    return trimmed.Substring(keyword.Length).Trim();
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Takes a screenshot of the current page on failure
        /// </summary>
        private async Task TakeScreenshot()
        {
            try
            {
                if (this.pageObject?.Page != null && !this.pageObject.Page.IsClosed)
                {
                    string screenshotFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_{SanitizeFileName(this.scenarioContext.ScenarioInfo.Title)}.png";
                    this.screenshotPath = Path.Combine(ScreenshotsDirectory, screenshotFileName);

                    await this.pageObject.Page.ScreenshotAsync(new PageScreenshotOptions { Path = this.screenshotPath });
                }
                else if (this.browserContext != null && !this.browserContext.IsClosed)
                {
                    // If the page is closed but the context is still open, create a new page to take a screenshot
                    try
                    {
                        var tempPage = await this.browserContext.NewPageAsync();
                        string screenshotFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_{SanitizeFileName(this.scenarioContext.ScenarioInfo.Title)}_fallback.png";
                        this.screenshotPath = Path.Combine(ScreenshotsDirectory, screenshotFileName);
                        await tempPage.ScreenshotAsync(new PageScreenshotOptions { Path = this.screenshotPath });
                        await tempPage.CloseAsync();
                    }
                    catch (Exception fallbackEx)
                    {
                        // Fallback screenshot creation also failed; leave screenshotPath empty.
                        this.screenshotPath = string.Empty;
                        TestContext.WriteLine($"[Hook.TakeScreenshot] Fallback screenshot failed: {fallbackEx.Message}");
                    }
                }
            }
            catch
            {
                // Log the error but don't throw; we still want the report to be generated even if screenshot fails
                this.screenshotPath = string.Empty;
            }
        }

        /// <summary>
        /// Records scenario execution details
        /// </summary>
        private void RecordScenarioReport(string traceFilePath)
        {
            string featureName = GetFeatureNameFromContext();
            string status = GetScenarioStatus();

            var report = new ScenarioReport
            {
                ScenarioName = this.scenarioContext.ScenarioInfo.Title,
                FeatureName = featureName,
                Status = status,
                ExecutionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                BrowserType = browserType,
                TraceFilePath = traceFilePath,
                ErrorMessage = GetErrorMessage(),
                ScreenshotPath = this.screenshotPath,
                Steps = this.scenarioSteps.Select(s => new StepExecution
                {
                    StepDefinitionType = s.StepDefinitionType,
                    Text = s.Text,
                    Status = s.Status,
                    ErrorMessage = s.ErrorMessage
                }).ToList()
            };

            lock (scenarioReportsLock)
            {
                scenarioReports.Add(report);
            }
        }

        private string GetFeatureNameFromContext()
        {
            try
            {
                if (this.featureContext != null)
                {
                    var featureInfo = this.featureContext.FeatureInfo;
                    if (featureInfo != null)
                    {
                        var featureName = featureInfo.Title;
                        if (!string.IsNullOrWhiteSpace(featureName))
                        {
                            return featureName;
                        }
                    }
                }

                if (this.scenarioContext?.ScenarioInfo != null)
                {
                    var scenarioInfoType = this.scenarioContext.ScenarioInfo.GetType();
                    var featureInfoProperty = scenarioInfoType.GetProperty("FeatureInfo");

                    if (featureInfoProperty != null)
                    {
                        var featureInfo = featureInfoProperty.GetValue(this.scenarioContext.ScenarioInfo);
                        if (featureInfo != null)
                        {
                            var titleProperty = featureInfo.GetType().GetProperty("Title");
                            if (titleProperty != null)
                            {
                                var featureName = titleProperty.GetValue(featureInfo)?.ToString();
                                if (!string.IsNullOrWhiteSpace(featureName))
                                {
                                    return featureName;
                                }
                            }
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private string GetScenarioStatus()
        {
            try
            {
                if (this.scenarioContext.TestError != null)
                {
                    return "Failed";
                }

                var status = this.scenarioContext.ScenarioExecutionStatus;

                return status switch
                {
                    ScenarioExecutionStatus.OK => "Passed",
                    ScenarioExecutionStatus.StepDefinitionPending => "Pending",
                    ScenarioExecutionStatus.UndefinedStep => "Undefined",
                    ScenarioExecutionStatus.BindingError => "Failed",
                    ScenarioExecutionStatus.TestError => "Failed",
                    _ => "Unknown"
                };
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetErrorMessage()
        {
            if (this.scenarioContext.TestError != null)
            {
                return this.scenarioContext.TestError.Message ?? "Unknown error";
            }
            return string.Empty;
        }

        /// <summary>
        /// Generates the HTML report by populating the external HTML/CSS templates
        /// with the collected scenario data, then writes the result to disk.
        /// </summary>
        private static void GenerateHtmlReport()
        {
            try
            {
                var htmlContent = GenerateHtmlContent();
                File.WriteAllText(ReportPath, htmlContent);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        /// <summary>
        /// Converts absolute path to relative path for HTML links
        /// </summary>
        private static string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return string.Empty;

            try
            {
                Uri reportUri = new Uri(ReportPath);
                Uri fileUri = new Uri(absolutePath);
                Uri relativeUri = reportUri.MakeRelativeUri(fileUri);
                return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                // Fallback: return just the filename
                return Path.GetFileName(absolutePath);
            }
        }

        /// <summary>
        /// Loads the external HTML template file. Falls back to a minimal inline
        /// template if the file is missing, so report generation never hard-fails.
        /// Logs a warning so a missing/uncopied template is easy to spot in test output.
        /// </summary>
        private static string LoadHtmlTemplate()
        {
            if (File.Exists(HtmlTemplatePath))
            {
                return File.ReadAllText(HtmlTemplatePath);
            }

            return "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><title>Test Report</title>" +
                   "<style>{{CSS_STYLES}}</style></head><body>" +
                   "<h1>Test Report</h1><p>Generated on: {{REPORT_DATE}}</p>" +
                   "<table>{{TABLE_ROWS}}</table></body></html>";
        }

        /// <summary>
        /// Loads the external CSS template file. Falls back to an empty stylesheet
        /// if the file is missing, so report generation never hard-fails.
        /// Logs a warning so a missing/uncopied stylesheet is easy to spot in test output.
        /// </summary>
        private static string LoadCssTemplate()
        {
            if (File.Exists(CssTemplatePath))
            {
                return File.ReadAllText(CssTemplatePath);
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds HTML content by populating the external template with data,
        /// keeping presentation (HTML/CSS) fully decoupled from this report logic.
        /// </summary>
        private static string GenerateHtmlContent()
        {
            var passedCount = scenarioReports.Count(s => s.Status == "Passed");
            var failedCount = scenarioReports.Count(s => s.Status == "Failed");
            var pendingCount = scenarioReports.Count(s => s.Status == "Pending");
            var undefinedCount = scenarioReports.Count(s => s.Status == "Undefined");
            var totalCount = scenarioReports.Count;

            var totalDuration = testRunEndTime - testRunStartTime;
            var durationString = $"{totalDuration.Hours:D2}h {totalDuration.Minutes:D2}m {totalDuration.Seconds:D2}s";

            // Extracting the partials here (before BuildTableRows runs) populates
            // reportPartials so every Build* method below has access to the
            // template's markup instead of hardcoded HTML.
            var htmlTemplate = ExtractReportPartials(LoadHtmlTemplate());
            var cssStyles = LoadCssTemplate();

            var pendingItem = pendingCount > 0
                ? FillTemplate(GetPartial("SUMMARY_ITEM"), new Dictionary<string, string>
                {
                    ["{{ITEM_CLASS}}"] = "pending",
                    ["{{ITEM_LABEL}}"] = "Pending",
                    ["{{ITEM_COUNT}}"] = pendingCount.ToString()
                })
                : string.Empty;

            var undefinedItem = undefinedCount > 0
                ? FillTemplate(GetPartial("SUMMARY_ITEM"), new Dictionary<string, string>
                {
                    ["{{ITEM_CLASS}}"] = "undefined",
                    ["{{ITEM_LABEL}}"] = "Undefined",
                    ["{{ITEM_COUNT}}"] = undefinedCount.ToString()
                })
                : string.Empty;

            var replacements = new Dictionary<string, string>
            {
                ["{{CSS_STYLES}}"] = cssStyles,
                ["{{REPORT_DATE}}"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["{{START_TIME}}"] = testRunStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ["{{END_TIME}}"] = testRunEndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ["{{DURATION}}"] = durationString,
                ["{{TOTAL_COUNT}}"] = totalCount.ToString(),
                ["{{PASSED_COUNT}}"] = passedCount.ToString(),
                ["{{FAILED_COUNT}}"] = failedCount.ToString(),
                ["{{PENDING_ITEM}}"] = pendingItem,
                ["{{UNDEFINED_ITEM}}"] = undefinedItem,
                ["{{SUCCESS_RATE}}"] = GetSuccessRate(passedCount, totalCount).ToString(),
                ["{{TABLE_ROWS}}"] = BuildTableRows(),
                ["{{PAGE_SIZE}}"] = GetReportPageSize().ToString()
            };

            return FillTemplate(htmlTemplate, replacements);
        }

        /// <summary>
        /// Names of every partial template Hook.cs expects to find inside the
        /// HTML template's &lt;template id="report-partials"&gt; block.
        /// </summary>
        private static readonly string[] PartialNames =
        {
            "ROW", "STEP_ITEM", "NO_STEPS",
            "ERROR_CELL_SCREENSHOT", "ERROR_CELL_TEXT", "ERROR_CELL_EMPTY", "SUMMARY_ITEM"
        };

        /// <summary>
        /// Built-in markup used for any partial that isn't defined in the loaded
        /// HTML template (e.g. an older template file, or the minimal inline
        /// fallback used when the template file is missing entirely). Keeps
        /// report generation resilient — presentation still lives in the
        /// template file for anyone who wants to customize it, but nothing
        /// breaks if a given block hasn't been added there yet.
        /// </summary>
        private static readonly Dictionary<string, string> DefaultPartials = new Dictionary<string, string>
        {
            ["ROW"] = """
                <tr>
                    <td>{{ROW_NUMBER}}</td>
                    <td><strong>{{FEATURE_NAME}}</strong></td>
                    <td>
                        <details class="scenario-details">
                            <summary>{{SCENARIO_NAME}}</summary>
                            <div class="steps-container">
                                <div class="steps-title">Test Steps ({{STEPS_COUNT}}):</div>
                                <ol class="steps-list">
                {{STEPS_HTML}}
                                </ol>
                            </div>
                        </details>
                    </td>
                    <td class="{{STATUS_CLASS}}">{{STATUS_ICON}} {{STATUS}}</td>
                    <td>{{BROWSER}}</td>
                    <td>{{EXECUTION_TIME}}</td>
                    <td><a href="{{TRACE_PATH}}" target="_blank" class="trace-link">View Trace</a></td>
                {{ERROR_CELL}}
                </tr>
                """,
            ["STEP_ITEM"] = """
                <li class="step-item {{STEP_STATUS_CLASS}}">
                    <span class="step-keyword">{{STEP_KEYWORD}}</span>
                    <span class="step-text">{{STEP_TEXT}}</span>
                    <span class="step-status">{{STEP_STATUS_ICON}}</span>
                </li>
                """,
            ["NO_STEPS"] = """<li class="step-item">No steps recorded</li>""",
            ["ERROR_CELL_SCREENSHOT"] = """
                <td class="error-message">
                    <details class="error-details">
                        <summary>{{SUMMARY_TEXT}}</summary>
                        <div class="screenshot-container">
                            <img src="{{SCREENSHOT_PATH}}" alt="Failure Screenshot" class="failure-screenshot">
                        </div>
                    </details>
                </td>
                """,
            ["ERROR_CELL_TEXT"] = """<td class="error-message">{{MESSAGE}}</td>""",
            ["ERROR_CELL_EMPTY"] = """<td class="error-message">-</td>""",
            ["SUMMARY_ITEM"] = """<div class="summary-item {{ITEM_CLASS}}"><span class="label">{{ITEM_LABEL}}</span><span class="value">{{ITEM_COUNT}}</span></div>"""
        };

        /// <summary>
        /// Partial templates extracted from the current HTML template for this
        /// report generation run. Populated by <see cref="ExtractReportPartials"/>.
        /// </summary>
        private static readonly Dictionary<string, string> reportPartials = new Dictionary<string, string>();

        /// <summary>
        /// Finds the &lt;template id="report-partials"&gt; block in the raw HTML
        /// template, pulls out each named TEMPLATE:/END: section into
        /// <see cref="reportPartials"/>, and returns the HTML with that whole
        /// block removed so it never ends up in the written report. If the
        /// block or a given partial isn't present, generation falls back to
        /// <see cref="DefaultPartials"/> — see <see cref="GetPartial"/>.
        /// </summary>
        private static string ExtractReportPartials(string html)
        {
            reportPartials.Clear();

            const string wrapperStart = "<template id=\"report-partials\">";
            const string wrapperEnd = "</template>";

            var wrapperStartIndex = html.IndexOf(wrapperStart, StringComparison.Ordinal);
            if (wrapperStartIndex < 0)
            {
                return html;
            }

            var wrapperEndIndex = html.IndexOf(wrapperEnd, wrapperStartIndex, StringComparison.Ordinal);
            if (wrapperEndIndex < 0)
            {
                return html;
            }

            var contentStart = wrapperStartIndex + wrapperStart.Length;
            var partialsBlock = html.Substring(contentStart, wrapperEndIndex - contentStart);

            foreach (var name in PartialNames)
            {
                var startMarker = $"<!-- TEMPLATE:{name} -->";
                var endMarker = $"<!-- END:{name} -->";

                var startIndex = partialsBlock.IndexOf(startMarker, StringComparison.Ordinal);
                var endIndex = partialsBlock.IndexOf(endMarker, StringComparison.Ordinal);

                if (startIndex >= 0 && endIndex > startIndex)
                {
                    var blockContentStart = startIndex + startMarker.Length;
                    reportPartials[name] = partialsBlock.Substring(blockContentStart, endIndex - blockContentStart).Trim();
                }
            }

            var removeEnd = wrapperEndIndex + wrapperEnd.Length;
            return html.Remove(wrapperStartIndex, removeEnd - wrapperStartIndex);
        }

        /// <summary>
        /// Returns the named partial as defined in the HTML template, or the
        /// built-in default markup if the template doesn't define it.
        /// </summary>
        private static string GetPartial(string name)
        {
            return reportPartials.TryGetValue(name, out var value) ? value : DefaultPartials[name];
        }

        /// <summary>
        /// Fills a template string by replacing every {{PLACEHOLDER}} key with
        /// its corresponding value. Shared by every Build* method so there's a
        /// single place that does template substitution.
        /// </summary>
        private static string FillTemplate(string template, Dictionary<string, string> values)
        {
            var sb = new StringBuilder(template);
            foreach (var kvp in values)
            {
                sb.Replace(kvp.Key, kvp.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds the &lt;tr&gt; rows for the results table. Each report is projected
        /// into its own row markup and the results are joined — no manual
        /// StringBuilder/AppendLine bookkeeping required.
        /// </summary>
        private static string BuildTableRows()
        {
            return string.Join(
                Environment.NewLine,
                scenarioReports.Select((report, index) => BuildTableRow(report, index + 1)));
        }

        /// <summary>
        /// Builds a single &lt;tr&gt; for one scenario report.
        /// </summary>
        private static string BuildTableRow(ScenarioReport report, int rowNumber)
        {
            var statusClass = report.Status switch
            {
                "Passed" => "status-passed",
                "Failed" => "status-failed",
                "Pending" => "status-pending",
                _ => "status-undefined"
            };

            var statusIcon = report.Status switch
            {
                "Passed" => "✓",
                "Failed" => "✗",
                "Pending" => "⏸",
                _ => "?"
            };

            var relativeTracePath = GetRelativePath(report.TraceFilePath);
            var relativeScreenshotPath = GetRelativePath(report.ScreenshotPath);
            var stepsHtml = BuildStepsHtml(report.Steps);
            var errorCell = BuildErrorCell(report, relativeScreenshotPath);

            return FillTemplate(GetPartial("ROW"), new Dictionary<string, string>
            {
                ["{{ROW_NUMBER}}"] = rowNumber.ToString(),
                ["{{FEATURE_NAME}}"] = HtmlEncode(report.FeatureName),
                ["{{SCENARIO_NAME}}"] = HtmlEncode(report.ScenarioName),
                ["{{STEPS_COUNT}}"] = report.Steps.Count.ToString(),
                ["{{STEPS_HTML}}"] = stepsHtml,
                ["{{STATUS_CLASS}}"] = statusClass,
                ["{{STATUS_ICON}}"] = statusIcon,
                ["{{STATUS}}"] = report.Status,
                ["{{BROWSER}}"] = report.BrowserType,
                ["{{EXECUTION_TIME}}"] = report.ExecutionTime,
                ["{{TRACE_PATH}}"] = relativeTracePath,
                ["{{ERROR_CELL}}"] = errorCell
            });
        }

        /// <summary>
        /// Builds the ordered list of &lt;li&gt; step items for a scenario.
        /// </summary>
        private static string BuildStepsHtml(List<StepExecution> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return GetPartial("NO_STEPS");
            }

            return string.Join(Environment.NewLine, steps.Select(BuildStepItem));
        }

        /// <summary>
        /// Builds a single &lt;li&gt; step item, including its optional error message.
        /// </summary>
        private static string BuildStepItem(StepExecution step)
        {
            var stepStatusClass = step.Status switch
            {
                "Passed" => "step-passed",
                "Failed" => "step-failed",
                _ => "step-pending"
            };

            var stepStatusIcon = step.Status switch
            {
                "Passed" => "✓",
                "Failed" => "✗",
                _ => "⏸"
            };

            return FillTemplate(GetPartial("STEP_ITEM"), new Dictionary<string, string>
            {
                ["{{STEP_STATUS_CLASS}}"] = stepStatusClass,
                ["{{STEP_KEYWORD}}"] = HtmlEncode(step.StepDefinitionType),
                ["{{STEP_TEXT}}"] = HtmlEncode(step.Text),
                ["{{STEP_STATUS_ICON}}"] = stepStatusIcon
            });
        }

        /// <summary>
        /// Builds the trailing error/screenshot &lt;td&gt; for a row based on scenario status.
        /// Only names which step failed — no exception message, stack trace, or
        /// Playwright diagnostic log is ever rendered into the report.
        /// </summary>
        private static string BuildErrorCell(ScenarioReport report, string relativeScreenshotPath)
        {
            if (report.Status == "Failed" && !string.IsNullOrEmpty(report.ScreenshotPath))
            {
                var failingStepText = GetFailingStepText(report);

                return FillTemplate(GetPartial("ERROR_CELL_SCREENSHOT"), new Dictionary<string, string>
                {
                    ["{{SUMMARY_TEXT}}"] = HtmlEncode($"Step failed: {failingStepText}"),
                    ["{{SCREENSHOT_PATH}}"] = relativeScreenshotPath
                });
            }

            if (report.Status == "Failed")
            {
                var failingStepText = GetFailingStepText(report);
                return FillTemplate(GetPartial("ERROR_CELL_TEXT"), new Dictionary<string, string>
                {
                    ["{{MESSAGE}}"] = HtmlEncode($"Step failed: {failingStepText}")
                });
            }

            return GetPartial("ERROR_CELL_EMPTY");
        }

        /// <summary>
        /// Returns the keyword + text of the first failed step in a scenario
        /// (e.g. "Then Payment page is loaded"), so the report can point at
        /// which step failed without including the exception message or any
        /// stack trace / diagnostic log.
        /// </summary>
        private static string GetFailingStepText(ScenarioReport report)
        {
            var failingStep = report.Steps?.FirstOrDefault(s => s.Status == "Failed");
            return failingStep != null
                ? $"{failingStep.StepDefinitionType} {failingStep.Text}".Trim()
                : "Unknown step";
        }

        private static string HtmlEncode(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text);
        }

        private static int GetSuccessRate(int passed, int total)
        {
            return total == 0 ? 0 : (int)((passed / (double)total) * 100);
        }

        /// <summary>
        /// Number of scenario rows shown per page in the HTML report before client-side
        /// pagination kicks in. Configurable via PLAYWRIGHT_ReportPageSize; defaults to 6.
        /// </summary>
        private static int GetReportPageSize()
        {
            var raw = Environment.GetEnvironmentVariable("PLAYWRIGHT_ReportPageSize");
            return int.TryParse(raw, out var pageSize) && pageSize > 0 ? pageSize : 6;
        }
    }

    public class ScenarioReport
    {
        public string ScenarioName { get; set; } = string.Empty;
        public string FeatureName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ExecutionTime { get; set; } = string.Empty;
        public string BrowserType { get; set; } = string.Empty;
        public string TraceFilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string ScreenshotPath { get; set; } = string.Empty;
        public List<StepExecution> Steps { get; set; } = new List<StepExecution>();
    }

    public class StepExecution
    {
        public string StepDefinitionType { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
