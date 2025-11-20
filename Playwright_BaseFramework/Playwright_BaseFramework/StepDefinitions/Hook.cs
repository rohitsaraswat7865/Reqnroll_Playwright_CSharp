using System;
using Microsoft.Playwright;
using Playwright_BaseFramework.Support;
using Reqnroll;

namespace Playwright_BaseFramework.StepDefinitions
{
    [Binding]
    public sealed class Hook
    {
        public PageObject pageObject;
        public IPlaywright playwright;
        public IBrowserContext browserContext;
        private ScenarioContext scenarioContext;

        public Hook(PageObject pageObject, ScenarioContext scenarioContext)
        {
            this.pageObject = pageObject;
            this.scenarioContext = scenarioContext;
        }

        [BeforeScenario]
        public async Task BeforeScenario()
        {
            // Read settings from environment variables (set via runsettings)
            bool headless = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Headless") ?? "true", out var h) ? h : true;
            int slowMo = int.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_SlowMo") ?? "1000", out var s) ? s :1000;
            bool screenshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Screenshots") ?? "true", out var ss) ? ss : true;
            bool snapshots = bool.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_Tracing_Snapshots") ?? "true", out var sp) ? sp : true;

            this.playwright = await Playwright.CreateAsync();
            var browserLaunchOptions = new BrowserTypeLaunchOptions()
            {
                Headless = false,
                SlowMo = slowMo
            };
            var browser = await playwright.Chromium.LaunchAsync(browserLaunchOptions);
            this.browserContext = await browser.NewContextAsync();
            await this.browserContext.Tracing.StartAsync(new()
            {
                Screenshots = screenshots,
                Snapshots = snapshots
            });
            this.pageObject.Page = await this.browserContext.NewPageAsync();
        }

        [AfterScenario]
        public async Task AfterScenario()
        {
            //saving traces in windows C drive
            var str = $"C:\\logs\\PlaywrightLogs\\{this.scenarioContext.ScenarioInfo.Title.Split("-")[0]}_{this.scenarioContext.ScenarioExecutionStatus.ToString()}.zip";
            await this.browserContext.Tracing.StopAsync(new()
            {
                Path = str
            });
            await this.browserContext.DisposeAsync();
            this.playwright.Dispose();
        }
    }
}