# ReqNRoll-BDD-Playwright-C# (Base Framework | Template)

This project is a comprehensive automation framework using [Playwright](https://playwright.dev/dotnet/) and [Reqnroll](https://reqnroll.net/) for end-to-end testing of web applications in C#. The framework demonstrates BDD-style test automation with modern best practices and comprehensive reporting.

**[25102025]** Migrated framework from Specflow to Reqnroll for enhanced functionality and community support.

## Features

- **Reqnroll (BDD)** - Gherkin-style feature files for behavior-driven test scenarios
- **Playwright** - Modern browser automation with Playwright for .NET
- **Page Object Model** - Encapsulated page interactions and locators
- **Parallel Execution** - Tests can be configured to run with NUnit in parallel
- **Comprehensive Reporting** - HTML report templates and browser trace/screenshot capture
- **External Data Support** - Excel-based test data via Reqnroll.ExternalData plugin
- **Trace & Screenshot Capture** - Trace snapshots and screenshots enabled by default
- **Configurable Execution** - Browser type, headless mode, slow motion, and base URL are defined in `Playwright.runsettings`

## Project Structure

```
Playwright_BaseFramework/
├── Features/
│   ├── SauceDemo1.feature          # Feature file for login and product page tests
│   ├── SauceDemo2.feature          # Feature file with external data source
│   ├── Test.xlsx                   # External test data file
│   └── [generated .feature.cs]     # Auto-generated step binding code
├── StepDefinitions/
│   └── SauceDemoStepDefinitions.cs # Step definitions for SauceDemo tests
├── Support/
│   ├── Hook.cs                     # Hooks for test setup/teardown, tracing, reporting
│   ├── PageObject.cs               # Base page object wrapper
│   ├── ReportTemplate.css          # HTML report stylesheet template
│   ├── ReportTemplate.html         # HTML report template for custom output
│   └── usings.cs                   # Global imports and parallelization config
├── Playwright_BaseFramework.csproj # Project file with NuGet dependencies
├── reqnroll.json                   # Reqnroll configuration with external data settings
├── Playwright.runsettings          # Test run settings (browser, headless, etc.)
└── bin/Debug/net10.0/
    ├── PlaywrightReport.html       # Test execution report
    ├── PlaywrightTraces/           # Browser execution traces
    └── PlaywrightScreenshots/      # Screenshots from test runs
```

## Technology Stack

- **.NET Target Framework**: .NET 10.0
- **Testing Framework**: NUnit 4.6.1
- **Playwright**: Microsoft.Playwright.NUnit 1.61.0
- **Reqnroll**: Reqnroll.NUnit 3.3.4
- **External Data**: Reqnroll.ExternalData 3.3.4
- **Test SDK**: Microsoft.NET.Test.Sdk 18.7.0

## Getting Started

### Prerequisites
- .NET 10.0 SDK or later
- PowerShell 5.1 or later (for Windows)

### Installation

1. **Clone the repository**
   ```powershell
   git clone <repository-url>
   cd Reqnroll_Playwright_CSharp\Playwright_BaseFramework\Playwright_BaseFramework
   ```

2. **Restore NuGet packages**
   ```powershell
   dotnet restore
   ```

3. **Clean and build the project**
   ```powershell
   dotnet clean
   dotnet build
   ```

4. **Install Playwright browsers**
   ```powershell
   pwsh bin\Debug\net10.0\playwright.ps1 install
   ```

5. **Run all tests**
   ```powershell
   dotnet test --settings Playwright.runsettings
   ```

5. **View test report**
   ```powershell
   # Open the generated HTML report
   Start-Process .\bin\Debug\net10.0\PlaywrightReport.html
   ```


## Test Artifacts

All test artifacts are stored in the output directory:

- **PlaywrightReport.html**: Interactive HTML report with scenario details and step logs
<img width="1919" height="905" alt="image" src="https://github.com/user-attachments/assets/2dc6225e-5db0-4e74-97aa-f1e2b2827fd5" />

- **PlaywrightTraces/**: Playwright trace files (.zip) for detailed debugging with Playwright Inspector
- **PlaywrightScreenshots/**: Screenshot images captured during test execution

## Parallel Execution

Tests are configured to run in parallel:

```csharp
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
```

- Runs up to 4 test fixtures in parallel
- Each scenario gets its own browser context for isolation

## Troubleshooting

### Browser Installation Issues
```powershell
# Reinstall browsers
pwsh bin\Debug\net10.0\playwright.ps1 install
```

### Test Execution Failures
- Check `PlaywrightReport.html` for detailed error logs
- Review `PlaywrightTraces/` files using Playwright Inspector
- Verify environment variables in `Playwright.runsettings`

### Debugging Tests
1. Set `PLAYWRIGHT_Headless` to `false` to see browser execution
2. Set `PLAYWRIGHT_SlowMo` to higher value (e.g., 5000) to slow execution
3. Check trace files in `PlaywrightTraces/` using Playwright Inspector

## Useful References

- [Playwright for .NET Documentation](https://playwright.dev/dotnet/docs/intro)
- [Reqnroll Documentation](https://docs.reqnroll.net/latest/quickstart/index.html)
- [Reqnroll External Data Plugin](https://docs.reqnroll.net/latest/plugins/external-data.html)
- [NUnit Documentation](https://docs.nunit.org/)
- [Playwright Trace Viewer](https://trace.playwright.dev/)

## License

MIT
