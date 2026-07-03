using Microsoft.Playwright;

namespace Playwright_BaseFramework.Support
{
    public class PageObject
    {
        // Represents tab#1 in browser. Nullable because it isn't available until
        // BeforeScenario creates it; consumers should only read it after that hook runs.
        private IPage? page;
        public IPage? Page
        {
            get => this.page;
            set => this.page = value;
        }
    }
}
