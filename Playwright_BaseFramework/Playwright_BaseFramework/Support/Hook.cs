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

            scenarioReports.Add(report);
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
        /// Generates HTML report with relative paths
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
        /// Builds HTML content with relative paths
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

            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Test Report</title>");
            sb.AppendLine(GetCssStyles());
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <div class=\"header\">");
            sb.AppendLine("            <h1>Test Report</h1>");
            sb.AppendLine("            <div class=\"header-info\">");
            sb.AppendLine($"                <p class=\"report-date\">Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine($"                <p class=\"runtime-info\">Start Time: {testRunStartTime:yyyy-MM-dd HH:mm:ss} | End Time: {testRunEndTime:yyyy-MM-dd HH:mm:ss} | Duration: {durationString}</p>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");

            sb.AppendLine("        <div class=\"summary\">");
            sb.AppendLine("            <div class=\"summary-card\">");
            sb.AppendLine($"                <div class=\"summary-item total\"><span class=\"label\">Total Tests</span><span class=\"value\">{totalCount}</span></div>");
            sb.AppendLine($"                <div class=\"summary-item passed\"><span class=\"label\">Passed</span><span class=\"value\">{passedCount}</span></div>");
            sb.AppendLine($"                <div class=\"summary-item failed\"><span class=\"label\">Failed</span><span class=\"value\">{failedCount}</span></div>");
            if (pendingCount > 0)
            {
                sb.AppendLine($"                <div class=\"summary-item pending\"><span class=\"label\">Pending</span><span class=\"value\">{pendingCount}</span></div>");
            }
            if (undefinedCount > 0)
            {
                sb.AppendLine($"                <div class=\"summary-item undefined\"><span class=\"label\">Undefined</span><span class=\"value\">{undefinedCount}</span></div>");
            }
            sb.AppendLine($"                <div class=\"summary-item success-rate\"><span class=\"label\">Success Rate</span><span class=\"value\">{GetSuccessRate(passedCount, totalCount)}%</span></div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");

            sb.AppendLine("        <div class=\"results-section\">");
            sb.AppendLine("            <h2>Test Results</h2>");
            sb.AppendLine("            <table class=\"results-table\">");
            sb.AppendLine("                <thead>");
            sb.AppendLine("                    <tr>");
            sb.AppendLine("                        <th>#</th>");
            sb.AppendLine("                        <th>Feature</th>");
            sb.AppendLine("                        <th>Scenario Name</th>");
            sb.AppendLine("                        <th>Status</th>");
            sb.AppendLine("                        <th>Browser</th>");
            sb.AppendLine("                        <th>Execution Time</th>");
            sb.AppendLine("                        <th>Trace File</th>");
            sb.AppendLine("                        <th>Error Details</th>");
            sb.AppendLine("                    </tr>");
            sb.AppendLine("                </thead>");
            sb.AppendLine("                <tbody>");

            int rowNumber = 1;
            foreach (var report in scenarioReports)
            {
                var statusClass = report.Status == "Passed" ? "status-passed" :
                                  report.Status == "Failed" ? "status-failed" :
                                  report.Status == "Pending" ? "status-pending" : "status-undefined";
                var statusIcon = report.Status == "Passed" ? "✓" :
                                report.Status == "Failed" ? "✗" :
                                report.Status == "Pending" ? "⏸" : "?";

                // Convert paths to relative
                string relativeTracePath = GetRelativePath(report.TraceFilePath);
                string relativeScreenshotPath = GetRelativePath(report.ScreenshotPath);

                sb.AppendLine("                    <tr>");
                sb.AppendLine($"                        <td>{rowNumber}</td>");
                sb.AppendLine($"                        <td><strong>{HtmlEncode(report.FeatureName)}</strong></td>");
                sb.AppendLine($"                        <td>");
                sb.AppendLine($"                            <details class=\"scenario-details\">");
                sb.AppendLine($"                                <summary>{HtmlEncode(report.ScenarioName)}</summary>");
                sb.AppendLine($"                                <div class=\"steps-container\">");
                sb.AppendLine($"                                    <div class=\"steps-title\">Test Steps ({report.Steps.Count}):</div>");
                sb.AppendLine($"                                    <ol class=\"steps-list\">");

                if (report.Steps != null && report.Steps.Count > 0)
                {
                    foreach (var step in report.Steps)
                    {
                        var stepStatusClass = step.Status == "Passed" ? "step-passed" :
                                            step.Status == "Failed" ? "step-failed" : "step-pending";
                        var stepStatusIcon = step.Status == "Passed" ? "✓" :
                                           step.Status == "Failed" ? "✗" : "⏸";

                        sb.AppendLine($"                                        <li class=\"step-item {stepStatusClass}\">");
                        sb.AppendLine($"                                            <span class=\"step-keyword\">{HtmlEncode(step.StepDefinitionType)}</span>");
                        sb.AppendLine($"                                            <span class=\"step-text\">{HtmlEncode(step.Text)}</span>");
                        sb.AppendLine($"                                            <span class=\"step-status\">{stepStatusIcon}</span>");

                        if (!string.IsNullOrEmpty(step.ErrorMessage))
                        {
                            sb.AppendLine($"                                            <div class=\"step-error\">{HtmlEncode(step.ErrorMessage)}</div>");
                        }

                        sb.AppendLine($"                                        </li>");
                    }
                }
                else
                {
                    sb.AppendLine($"                                        <li class=\"step-item\">No steps recorded</li>");
                }

                sb.AppendLine($"                                    </ol>");
                sb.AppendLine($"                                </div>");
                sb.AppendLine($"                            </details>");
                sb.AppendLine($"                        </td>");
                sb.AppendLine($"                        <td class=\"{statusClass}\">{statusIcon} {report.Status}</td>");
                sb.AppendLine($"                        <td>{report.BrowserType}</td>");
                sb.AppendLine($"                        <td>{report.ExecutionTime}</td>");
                sb.AppendLine($"                        <td><a href=\"{relativeTracePath}\" target=\"_blank\" class=\"trace-link\">View Trace</a></td>");

                if (report.Status == "Failed" && !string.IsNullOrEmpty(report.ScreenshotPath))
                {
                    sb.AppendLine($"                        <td class=\"error-message\">");
                    sb.AppendLine($"                            <details class=\"error-details\">");
                    sb.AppendLine($"                                <summary>{HtmlEncode(string.IsNullOrEmpty(report.ErrorMessage) ? "View Screenshot" : report.ErrorMessage)}</summary>");
                    sb.AppendLine($"                                <div class=\"screenshot-container\">");
                    sb.AppendLine($"                                    <img src=\"{relativeScreenshotPath}\" alt=\"Failure Screenshot\" class=\"failure-screenshot\">");
                    sb.AppendLine($"                                </div>");
                    sb.AppendLine($"                            </details>");
                    sb.AppendLine($"                        </td>");
                }
                else if (report.Status == "Failed")
                {
                    sb.AppendLine($"                        <td class=\"error-message\">{HtmlEncode(string.IsNullOrEmpty(report.ErrorMessage) ? "-" : report.ErrorMessage)}</td>");
                }
                else
                {
                    sb.AppendLine($"                        <td class=\"error-message\">-</td>");
                }

                sb.AppendLine("                    </tr>");
                rowNumber++;
            }

            sb.AppendLine("                </tbody>");
            sb.AppendLine("            </table>");
            sb.AppendLine("        </div>");

            sb.AppendLine("        <div class=\"footer\">");
            sb.AppendLine("            <p>Powered by Playwright & Reqnroll</p>");
            sb.AppendLine("        </div>");

            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private static string HtmlEncode(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text);
        }

        private static string GetCssStyles()
        {
            return @"
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }

        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: white;
            min-height: 100vh;
            padding: 20px;
        }

        .container {
            max-width: 90%;
            width: 90%;
            margin: 0 auto;
            background: white;
            border-radius: 10px;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
            overflow: hidden;
            border: 1px solid #e0e0e0;
        }

        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 15px 20px;
            text-align: center;
        }

        .header h1 {
            font-size: 1.8em;
            margin-bottom: 8px;
        }

        .header-info {
            font-size: 0.85em;
        }

        .report-date {
            opacity: 0.95;
            margin-bottom: 5px;
        }

        .runtime-info {
            opacity: 0.9;
            font-weight: 500;
        }

        .summary {
            padding: 30px 20px;
            background: #fafafa;
        }

        .summary-card {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 20px;
        }

        .summary-item {
            background: white;
            padding: 20px;
            border-radius: 8px;
            text-align: center;
            box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
            border-left: 5px solid #ccc;
            border: 1px solid #e0e0e0;
        }

        .summary-item.total {
            border-left-color: #6c757d;
        }

        .summary-item.passed {
            border-left-color: #28a745;
        }

        .summary-item.failed {
            border-left-color: #dc3545;
        }

        .summary-item.pending {
            border-left-color: #ffc107;
        }

        .summary-item.undefined {
            border-left-color: #17a2b8;
        }

        .summary-item.success-rate {
            border-left-color: #007bff;
        }

        .summary-item .label {
            display: block;
            font-size: 0.85em;
            color: #666;
            margin-bottom: 10px;
            font-weight: 500;
        }

        .summary-item .value {
            display: block;
            font-size: 2em;
            font-weight: bold;
            color: #333;
        }

        .results-section {
            padding: 30px 20px;
        }

        .results-section h2 {
            margin-bottom: 20px;
            color: #333;
            font-size: 1.8em;
        }

        .results-table {
            width: 100%;
            border-collapse: collapse;
            background: white;
            font-size: 0.95em;
            border: 1px solid #e0e0e0;
        }

        .results-table thead {
            background: #667eea;
            color: white;
            position: sticky;
            top: 0;
        }

        .results-table th {
            padding: 15px;
            text-align: left;
            font-weight: 600;
            border-right: 1px solid rgba(255, 255, 255, 0.2);
        }

        .results-table td {
            padding: 12px 15px;
            border-bottom: 1px solid #e0e0e0;
            border-right: 1px solid #e0e0e0;
        }

        .results-table tbody tr:hover {
            background: #f5f5f5;
        }

        .results-table tbody tr:nth-child(even) {
            background: #fafafa;
        }

        .scenario-details {
            cursor: pointer;
        }

        .scenario-details summary {
            color: #333;
            font-weight: 600;
            padding: 8px;
            background: #e8f0fe;
            border-radius: 4px;
            user-select: none;
            transition: all 0.3s;
        }

        .scenario-details summary:hover {
            background: #d2e3fc;
        }

        .scenario-details[open] summary {
            background: #667eea;
            color: white;
        }

        .steps-container {
            margin-top: 15px;
            padding: 15px;
            background: #f9f9f9;
            border: 1px solid #ddd;
            border-radius: 4px;
            max-height: 250px;
            overflow-y: auto;
            min-width: 600px;
        }

        .steps-title {
            font-weight: 700;
            color: #333;
            margin-bottom: 10px;
            font-size: 0.9em;
        }

        .steps-list {
            margin-left: 20px;
            line-height: 1.6;
        }

        .step-item {
            color: #555;
            font-size: 0.9em;
            margin-bottom: 6px;
            padding: 6px 10px;
            background: white;
            border-left: 3px solid #667eea;
            border-radius: 2px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .step-keyword {
            font-weight: 700;
            color: #667eea;
            min-width: 60px;
        }

        .step-text {
            flex: 1;
        }

        .step-status {
            font-size: 1.1em;
            font-weight: bold;
        }

        .step-item.step-passed {
            border-left-color: #28a745;
        }

        .step-item.step-passed .step-status {
            color: #28a745;
        }

        .step-item.step-failed {
            border-left-color: #dc3545;
            background: #fff5f5;
        }

        .step-item.step-failed .step-status {
            color: #dc3545;
        }

        .step-item.step-pending {
            border-left-color: #ffc107;
        }

        .step-item.step-pending .step-status {
            color: #ffc107;
        }

        .step-error {
            width: 100%;
            margin-top: 8px;
            padding: 8px;
            background: #ffe6e6;
            border-left: 3px solid #dc3545;
            color: #dc3545;
            font-size: 0.85em;
            border-radius: 2px;
        }

        .status-passed {
            color: #28a745;
            font-weight: bold;
        }

        .status-failed {
            color: #dc3545;
            font-weight: bold;
        }

        .status-pending {
            color: #ffc107;
            font-weight: bold;
        }

        .status-undefined {
            color: #17a2b8;
            font-weight: bold;
        }

        .trace-link {
            color: #007bff;
            text-decoration: none;
            font-weight: 500;
            padding: 5px 10px;
            border: 1px solid #007bff;
            border-radius: 4px;
            transition: all 0.3s;
            display: inline-block;
        }

        .trace-link:hover {
            background: #007bff;
            color: white;
        }

        .error-message {
            color: #dc3545;
            font-size: 0.85em;
            max-width: 300px;
            word-break: break-word;
        }

        .error-details {
            cursor: pointer;
        }

        .error-details summary {
            color: #dc3545;
            font-weight: 600;
            padding: 8px;
            background: #ffe6e6;
            border-radius: 4px;
            user-select: none;
            transition: all 0.3s;
        }

        .error-details summary:hover {
            background: #ffc9c9;
        }

        .error-details[open] summary {
            background: #ff9999;
            color: white;
        }

        .screenshot-container {
            margin-top: 15px;
            padding: 10px;
            background: #f9f9f9;
            border: 1px solid #ddd;
            border-radius: 4px;
            max-height: 400px;
            overflow-y: auto;
        }

        .failure-screenshot {
            max-width: 100%;
            height: auto;
            border: 1px solid #ccc;
            border-radius: 4px;
            display: block;
        }

        .footer {
            background: #fafafa;
            padding: 20px;
            text-align: center;
            color: #666;
            font-size: 0.945em;
            border-top: 1px solid #e0e0e0;
        }

        @media (max-width: 1024px) {
            .container {
                max-width: 95%;
                width: 95%;
            }

            .results-table {
                font-size: 0.85em;
            }

            .results-table th,
            .results-table td {
                padding: 10px;
            }

            .steps-container {
                max-height: 200px;
                min-width: 400px;
            }
        }

        @media (max-width: 768px) {
            .container {
                max-width: 98%;
                width: 98%;
            }

            .header h1 {
                font-size: 1.5em;
            }

            .header-info {
                font-size: 0.75em;
            }

            .results-table {
                font-size: 0.75em;
            }

            .summary-card {
                grid-template-columns: 1fr 1fr;
            }

            .steps-container {
                max-height: 180px;
                min-width: 300px;
                font-size: 0.85em;
            }
        }
    </style>";
        }

        private static int GetSuccessRate(int passed, int total)
        {
            return total == 0 ? 0 : (int)((passed / (double)total) * 100);
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