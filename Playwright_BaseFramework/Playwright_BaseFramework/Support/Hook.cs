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
        public PageObject pageObject;
        public static IPlaywright? playwright;
        public static IBrowser? browser;
        public IBrowserContext browserContext;
        private ScenarioContext scenarioContext;
        private FeatureContext featureContext;
        private static string browserType;
        private static List<ScenarioReport> scenarioReports = new List<ScenarioReport>();
        private static readonly object scenarioReportsLock = new object();
        private string traceFilePath;
        private string screenshotPath;
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
                Path = "auth.json"
            });
        }

        [BeforeScenario]
        public async Task BeforeScenario()
        {
            this.scenarioSteps.Clear();
            CaptureScenarioStepsFromFeature();

            bool screenshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Screenshots") ?? "true", out var ss) ? ss : true;
            bool snapshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Snapshots") ?? "true", out var sp) ? sp : true;

            this.browserContext = await browser.NewContextAsync(new BrowserNewContextOptions()
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
        public void AfterStep()
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
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [AfterScenario]
        public async Task AfterScenario()
        {
            // Take screenshot only if scenario failed
            if (this.scenarioContext.ScenarioExecutionStatus != ScenarioExecutionStatus.OK)
            {
                await TakeScreenshot();
            }

            // Save trace file with relative path
            var traceFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_{SanitizeFileName(this.scenarioContext.ScenarioInfo.Title)}_{this.scenarioContext.ScenarioExecutionStatus}.zip";
            this.traceFilePath = Path.Combine(TracesDirectory, traceFileName);

            await this.browserContext.Tracing.StopAsync(new()
            {
                Path = this.traceFilePath
            });

            // Record scenario report
            RecordScenarioReport(this.traceFilePath);

            await this.browserContext.DisposeAsync();
        }

        [AfterTestRun]
        public static async Task AfterTestRun()
        {
            testRunEndTime = DateTime.Now;

            await browser.DisposeAsync();
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
                if (this.pageObject?.Page != null)
                {
                    string screenshotFileName = $"{DateTime.Now.ToFileTime()}_{browserType}_{SanitizeFileName(this.scenarioContext.ScenarioInfo.Title)}.png";
                    this.screenshotPath = Path.Combine(ScreenshotsDirectory, screenshotFileName);

                    await this.pageObject.Page.ScreenshotAsync(new PageScreenshotOptions { Path = this.screenshotPath });
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
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
            catch (Exception ex)
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

            Console.WriteLine($"[Hook] WARNING: HTML template not found at '{HtmlTemplatePath}'. " +
                               "Falling back to a minimal inline template. Ensure Templates\\ReportTemplate.html " +
                               "has CopyToOutputDirectory set in the .csproj.");

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

            Console.WriteLine($"[Hook] WARNING: CSS template not found at '{CssTemplatePath}'. " +
                               "The report will render unstyled. Ensure Templates\\ReportTemplate.css " +
                               "has CopyToOutputDirectory set in the .csproj.");

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

            var htmlTemplate = LoadHtmlTemplate();
            var cssStyles = LoadCssTemplate();

            var pendingItem = pendingCount > 0
                ? $"<div class=\"summary-item pending\"><span class=\"label\">Pending</span><span class=\"value\">{pendingCount}</span></div>"
                : string.Empty;

            var undefinedItem = undefinedCount > 0
                ? $"<div class=\"summary-item undefined\"><span class=\"label\">Undefined</span><span class=\"value\">{undefinedCount}</span></div>"
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

            var sb = new StringBuilder(htmlTemplate);
            foreach (var kvp in replacements)
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

            return $"""
                                <tr>
                                    <td>{rowNumber}</td>
                                    <td><strong>{HtmlEncode(report.FeatureName)}</strong></td>
                                    <td>
                                        <details class="scenario-details">
                                            <summary>{HtmlEncode(report.ScenarioName)}</summary>
                                            <div class="steps-container">
                                                <div class="steps-title">Test Steps ({report.Steps.Count}):</div>
                                                <ol class="steps-list">
                                {stepsHtml}
                                                </ol>
                                            </div>
                                        </details>
                                    </td>
                                    <td class="{statusClass}">{statusIcon} {report.Status}</td>
                                    <td>{report.BrowserType}</td>
                                    <td>{report.ExecutionTime}</td>
                                    <td><a href="{relativeTracePath}" target="_blank" class="trace-link">View Trace</a></td>
                                {errorCell}
                                </tr>
                        """;
        }

        /// <summary>
        /// Builds the ordered list of &lt;li&gt; step items for a scenario.
        /// </summary>
        private static string BuildStepsHtml(List<StepExecution> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return """                                        <li class="step-item">No steps recorded</li>""";
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

            var errorHtml = string.IsNullOrEmpty(step.ErrorMessage)
                ? string.Empty
                : $"""

                                                            <div class="step-error">{HtmlEncode(step.ErrorMessage)}</div>
                        """;

            return $"""
                                                        <li class="step-item {stepStatusClass}">
                                                            <span class="step-keyword">{HtmlEncode(step.StepDefinitionType)}</span>
                                                            <span class="step-text">{HtmlEncode(step.Text)}</span>
                                                            <span class="step-status">{stepStatusIcon}</span>{errorHtml}
                                                        </li>
                        """;
        }

        /// <summary>
        /// Builds the trailing error/screenshot &lt;td&gt; for a row based on scenario status.
        /// </summary>
        private static string BuildErrorCell(ScenarioReport report, string relativeScreenshotPath)
        {
            if (report.Status == "Failed" && !string.IsNullOrEmpty(report.ScreenshotPath))
            {
                var summaryText = string.IsNullOrEmpty(report.ErrorMessage) ? "View Screenshot" : report.ErrorMessage;

                return $"""
                                    <td class="error-message">
                                        <details class="error-details">
                                            <summary>{HtmlEncode(summaryText)}</summary>
                                            <div class="screenshot-container">
                                                <img src="{relativeScreenshotPath}" alt="Failure Screenshot" class="failure-screenshot">
                                            </div>
                                        </details>
                                    </td>
                        """;
            }

            if (report.Status == "Failed")
            {
                var message = string.IsNullOrEmpty(report.ErrorMessage) ? "-" : report.ErrorMessage;
                return $"""                                    <td class="error-message">{HtmlEncode(message)}</td>""";
            }

            return """                                    <td class="error-message">-</td>""";
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
        public string ScenarioName { get; set; }
        public string FeatureName { get; set; }
        public string Status { get; set; }
        public string ExecutionTime { get; set; }
        public string BrowserType { get; set; }
        public string TraceFilePath { get; set; }
        public string ErrorMessage { get; set; }
        public string ScreenshotPath { get; set; }
        public List<StepExecution> Steps { get; set; } = new List<StepExecution>();
    }

    public class StepExecution
    {
        public string StepDefinitionType { get; set; }
        public string Text { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }
}
