FROM mcr.microsoft.com/dotnet/sdk:10.0

# Playwright engine(s) to install: chromium (Chrome/Edge), firefox, webkit (Safari).
ARG PLAYWRIGHT_BROWSERS=chromium

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src

# Restore first so the NuGet layer is cached until the .csproj changes
COPY Playwright_BaseFramework/Playwright_BaseFramework/Playwright_BaseFramework.csproj Playwright_BaseFramework/Playwright_BaseFramework/
RUN dotnet restore Playwright_BaseFramework/Playwright_BaseFramework/Playwright_BaseFramework.csproj

COPY . .

RUN dotnet build Playwright_BaseFramework/Playwright_BaseFramework/Playwright_BaseFramework.csproj -c Release --no-restore

# Install browsers + OS dependencies using the Playwright version referenced by the project
RUN pwsh Playwright_BaseFramework/Playwright_BaseFramework/bin/Release/net10.0/playwright.ps1 install --with-deps ${PLAYWRIGHT_BROWSERS}

ENV PROJECT_DIR=/src/Playwright_BaseFramework/Playwright_BaseFramework \
    RESULTS_DIR=/results

# Runsettings values override process env vars, so patch a copy of the local settings:
# the container has no display (force headless) and SlowMo only slows CI down.
# Report, traces and screenshots are kept together so the report's relative links still work.
CMD bash -c ' \
    OUT="$PROJECT_DIR/bin/Release/net10.0"; \
    mkdir -p "$RESULTS_DIR/report" "$RESULTS_DIR/trx" "$RESULTS_DIR/logs"; \
    sed -e "s#<PLAYWRIGHT_Headless>[^<]*<#<PLAYWRIGHT_Headless>${PLAYWRIGHT_Headless:-true}<#" \
        -e "s#<PLAYWRIGHT_SlowMo>[^<]*<#<PLAYWRIGHT_SlowMo>${PLAYWRIGHT_SlowMo:-0}<#" \
        -e "s#<PLAYWRIGHT_BrowserType>[^<]*<#<PLAYWRIGHT_BrowserType>${PLAYWRIGHT_BrowserType:-Chrome}<#" \
        "$PROJECT_DIR/Playwright.runsettings" > /tmp/ci.runsettings; \
    dotnet test "$PROJECT_DIR/Playwright_BaseFramework.csproj" -c Release --no-build \
        --settings /tmp/ci.runsettings \
        --results-directory "$RESULTS_DIR/trx" \
        --logger "trx;LogFileName=test-results.trx" \
        --logger "console;verbosity=normal" \
        2>&1 | tee "$RESULTS_DIR/logs/test-output.log"; \
    code=${PIPESTATUS[0]}; \
    cp "$OUT/PlaywrightReport.html" "$RESULTS_DIR/report/" 2>/dev/null || echo "PlaywrightReport.html was not generated"; \
    cp -r "$OUT/PlaywrightTraces" "$OUT/PlaywrightScreenshots" "$RESULTS_DIR/report/" 2>/dev/null; \
    exit $code'
