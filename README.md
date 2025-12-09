# ReqNRoll-BDD-Playwright-C# (Base Framework | Template)

This project is a comprehensive automation framework using [Playwright](https://playwright.dev/dotnet/) and [Reqnroll](https://reqnroll.net/) for end-to-end testing of web applications in C#. The framework demonstrates BDD-style test automation with modern best practices and comprehensive reporting.

**[25102025]** Migrated framework from Specflow to Reqnroll for enhanced functionality and community support.

## Features

- **Reqnroll (BDD)** - Gherkin-style feature files for behavior-driven test scenarios
- **Playwright** - Modern browser automation with multi-browser support (Chrome, Edge, Firefox, Safari)
- **Page Object Model** - Encapsulated page interactions and locators
- **Parallel Execution** - Tests run in parallel with NUnit (4 parallel fixtures)
- **Comprehensive Reporting** - HTML test reports with screenshots, traces, and detailed step logs
- **External Data Support** - Excel-based test data via Reqnroll.ExternalData plugin
- **Trace & Screenshot Capture** - Automatic trace files and screenshots for debugging
- **Cross-Browser Testing** - Configurable browser type, headless mode, and slow-motion execution

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
- **Testing Framework**: NUnit 4.4.0
- **Playwright**: 1.57.0
- **Reqnroll**: 3.2.1
- **External Data**: Reqnroll.ExternalData 3.2.1
- **Test SDK**: Microsoft.NET.Test.Sdk 18.0.1

## Getting Started

### Prerequisites
- .NET 10.0 SDK or later
- PowerShell 5.1 or later (for Windows)

### Installation

1. **Clone the repository**
   ```powershell
   git clone <repository-url>
   cd Reqnroll_Playwright_CSharp\Playwright_BaseFramework
   ```

2. **Restore NuGet packages**
   ```powershell
   dotnet restore
   ```

3. **Install Playwright browsers**
   ```powershell
   pwsh bin\Debug\net10.0\playwright.ps1 install
   ```

4. **Run all tests**
   ```powershell
   dotnet test --settings Playwright.runsettings
   ```

5. **View test report**
   ```powershell
   # Open the generated HTML report
   & "bin\Debug\net10.0\PlaywrightReport.html"
   ```
   <img width="854" height="418" alt="image" src="https://github.com/user-attachments/assets/8f0f8874-3343-4f1e-8942-ba4b7a1cd011" />


## Test Artifacts

All test artifacts are stored in the output directory:

- **PlaywrightReport.html**: Interactive HTML report with scenario details and step logs
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
