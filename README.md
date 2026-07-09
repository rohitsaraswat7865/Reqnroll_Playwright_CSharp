<div align="center">

# ReqNRoll BDD Playwright C#

### Base Framework Template

**A comprehensive, production-ready end-to-end testing framework combining BDD and modern browser automation**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Playwright](https://img.shields.io/badge/Playwright-1.61.0-2EAD33?style=for-the-badge&logo=playwright&logoColor=white)](https://playwright.dev/dotnet/)
[![Reqnroll](https://img.shields.io/badge/Reqnroll-3.3.4-FF6C37?style=for-the-badge)](https://reqnroll.net/)
[![NUnit](https://img.shields.io/badge/NUnit-4.6.1-1094AB?style=for-the-badge)](https://nunit.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)

</div>

---

> 📢 **[25.10.2025]** Migrated framework from SpecFlow → **Reqnroll** for enhanced functionality and community support.

---

## ✨ Features

<table>
<tr>
<td width="50%">

**🥒 Reqnroll (BDD)**
Gherkin-style feature files for behavior-driven test scenarios

**🎭 Playwright**
Modern, fast browser automation with Playwright for .NET

**📦 Page Object Model**
Encapsulated page interactions and locators for maintainability

**⚡ Parallel Execution**
Tests run in parallel via NUnit for faster feedback

**📊 Comprehensive Reporting**
HTML report templates with browser trace & screenshot capture

**📁 External Data Support**
Excel-based test data via `Reqnroll.ExternalData` plugin

**🔍 Trace & Screenshot Capture**
Enabled by default for effortless debugging

**⚙️ Configurable Execution**
Browser type, headless mode, slow-mo & base URL via `.runsettings`

</td>
</tr>
</table>

---

## 🗂️ Project Structure

```
Playwright_BaseFramework/
├── 📁 Features/
│   ├── 📄 SauceDemo1.feature          # Login & product page tests
│   ├── 📄 SauceDemo2.feature          # Tests using external data source
│   ├── 📊 Test.xlsx                   # External test data file
│   └── 📄 [generated .feature.cs]     # Auto-generated step bindings
├── 📁 StepDefinitions/
│   └── 🧩 SauceDemoStepDefinitions.cs # Step definitions for SauceDemo tests
├── 📁 Support/
│   ├── 🪝 Hook.cs                     # Setup/teardown, tracing, reporting hooks
│   ├── 🧱 PageObject.cs               # Base page object wrapper
│   ├── 🎨 ReportTemplate.css          # HTML report stylesheet
│   ├── 🖼️ ReportTemplate.html         # HTML report template
│   └── ⚙️ usings.cs                   # Global imports & parallelization config
├── 🔧 Playwright_BaseFramework.csproj # Project file with NuGet dependencies
├── 🔧 reqnroll.json                   # Reqnroll config with external data settings
├── 🔧 Playwright.runsettings          # Run settings (browser, headless, etc.)
└── 📁 bin/Debug/net10.0/
    ├── 📊 PlaywrightReport.html       # Test execution report
    ├── 🎬 PlaywrightTraces/           # Browser execution traces
    └── 📸 PlaywrightScreenshots/      # Screenshots from test runs
```

---

## 🧰 Technology Stack

| Component | Package | Version |
|---|---|---|
| 🎯 Target Framework | .NET | `10.0` |
| 🧪 Testing Framework | NUnit | `4.6.1` |
| 🎭 Browser Automation | Microsoft.Playwright.NUnit | `1.61.0` |
| 🥒 BDD Engine | Reqnroll.NUnit | `3.3.4` |
| 📁 External Data | Reqnroll.ExternalData | `3.3.4` |
| 🛠️ Test SDK | Microsoft.NET.Test.Sdk | `18.7.0` |

---

## 🚀 Follow these steps:

### ✅ Prerequisites

- .NET 10.0 SDK or later
- PowerShell 5.1 or later (for Windows)

### 📥 Installation

**1️⃣ Clone the repository**

```bash
git clone <repository-url>
cd Reqnroll_Playwright_CSharp\Playwright_BaseFramework\Playwright_BaseFramework
```

**2️⃣ Restore NuGet packages**

```bash
dotnet restore
```

**3️⃣ Clean and build the project**

```bash
dotnet clean
dotnet build
```

**4️⃣ Install Playwright browsers**

```powershell
pwsh bin\Debug\net10.0\playwright.ps1 install
```

**5️⃣ Run all tests**

```bash
dotnet test --settings Playwright.runsettings
```

**6️⃣ View the test report**

```powershell
Start-Process .\bin\Debug\net10.0\PlaywrightReport.html
```
<img width="987" height="1026" alt="image" src="https://github.com/user-attachments/assets/d1c6d74f-8e18-45a0-81c5-38ee4e785d3a" />

---

## 📊 Test Artifacts

All test artifacts are stored in the output directory:

| Artifact | Description |
|---|---|
| 📈 **PlaywrightReport.html** | Interactive HTML report with scenario details and step logs |
| 🎬 **PlaywrightTraces/** | Playwright trace files (`.zip`) for detailed debugging with Playwright Inspector |
| 📸 **PlaywrightScreenshots/** | Screenshot images captured during test execution |

### 📋 Report Structure

The generated HTML report includes a **summary header** with total tests, passed count, failed count, and overall success rate. Each scenario is listed in a table with feature name, scenario title, status, browser, execution time, and action links for trace files. The report also supports **pagination** when many scenarios are present — making it easy to review long test runs.

<img width="1897" height="914" alt="Screenshot 2026-07-03 140030" src="https://github.com/user-attachments/assets/9092430e-dce9-40ee-bf11-9b142b7d73df" />

<img width="1892" height="907" alt="Screenshot 2026-07-03 173256" src="https://github.com/user-attachments/assets/3a6c03a9-0642-4f46-9b07-3fd8860fb3b1" />
---

## ⚡ Parallel Execution

Tests are configured to run in parallel out of the box:

```csharp
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
```

> 🔹 Runs up to **4 test fixtures** in parallel
> 🔹 Each scenario gets its **own browser context** for isolation

---

## 🛠️ Troubleshooting

<details>
<summary><b>🌐 Browser Installation Issues</b></summary>
<br>

```powershell
# Reinstall browsers
pwsh bin\Debug\net10.0\playwright.ps1 install
```

</details>

<details>
<summary><b>❌ Test Execution Failures</b></summary>
<br>

- Check `PlaywrightReport.html` for errors
- Review `PlaywrightTraces/` files using Playwright Trace Viewer
- Verify environment variables in `Playwright.runsettings`

</details>

<details>
<summary><b>🐞 Debugging Tests</b></summary>
<br>

- Set `PLAYWRIGHT_Headless=false` to watch the browser execute the tests
- Set `PLAYWRIGHT_SlowMo` to a higher value (e.g., `5000`) to slow down execution — useful for observing behavior and debugging on slow or unstable networks
- Check trace files in `PlaywrightTraces/` using the Playwright Trace Viewer for execution logs, screenshots, network activity, and DOM snapshots

</details>

---

## 📚 Useful References

- 🎭 [Playwright for .NET Documentation](https://playwright.dev/dotnet/)
- 🥒 [Reqnroll Documentation](https://reqnroll.net/)
- 📁 [Reqnroll External Data Plugin](https://reqnroll.net/)
- 🧪 [NUnit Documentation](https://docs.nunit.org/)
- 🔍 [Playwright Trace Viewer](https://playwright.dev/dotnet/docs/trace-viewer)

---

<div align="center">



---

</div>
