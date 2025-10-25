using Microsoft.Playwright;
using NUnit.Framework.Constraints;
using Playwright_BaseFramework.Support;
using Reqnroll;
using System.IO;


namespace Playwright_BaseFramework.StepDefinitions
{
    [Binding]
    public sealed class SauceDemoStepDefinitions
    {
        private ScenarioContext scenarioContext;
        public PageObject pageObject;

        public SauceDemoStepDefinitions(PageObject pageObject, ScenarioContext scenarioContext)
        {
            this.pageObject = pageObject;
            this.scenarioContext = scenarioContext;
        }

        [Given(@"Login page is loaded")]
        public async Task GivenLoginPageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.GotoAsync("https://www.saucedemo.com/", new()
                {
                    Timeout = 40_000
                });

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

        }

        [When(@"Enter username as (.*) in login page")]
        public async Task WhenEnterUsernameInLoginPage(string userName)
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"username\"]").ClickAsync();
                await this.pageObject.Page.Locator("[data-test=\"username\"]").FillAsync(userName);                
            }
            catch(Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Enter password in login page")]
        public async Task WhenEnterPasswordInLoginPage()
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"password\"]").ClickAsync();
                await this.pageObject.Page.Locator("[data-test=\"password\"]").FillAsync("secret_sauce");

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Click submit button in login page")]
        public async Task WhenClickSubmitButtonInLoginPage()
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"login-button\"]").ClickAsync(new()
                {
                    Delay = 5_000
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [Then(@"Product page is loaded")]
        public async Task ThenProductPageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.WaitForURLAsync("**/inventory.html", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 15_000
                });
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"title\"]")).ToBeVisibleAsync(new()
                {
                    Timeout = 30_000,
                    Visible = true
                });
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"title\"]")).ToContainTextAsync("Products", new()
                {
                    Timeout = 10_000                    
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Select following (.*) in product page")]
        public async Task WhenSelectFollowingProductsInProductPage(string productNameList)
        {
            try
            {
                var arr = productNameList.Split(",");
                var count = arr.Length.ToString();
                foreach(var productName in arr)
                {
                    await this.pageObject.Page.Locator($"xpath=//div[contains(text(),'{productName}')]/ancestor::div[@class='inventory_item_description']//div[@class='pricebar']/button", new()
                    {
                        HasText = "Add to cart"
                    })
                        .ClickAsync(new()
                    {
                        Button = MouseButton.Left,
                        Timeout = 10_000
                    });
                }
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"shopping-cart-badge\"]")).ToContainTextAsync(count);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Click on cart icon in product page")]
        public async Task WhenClickOnCartIconInProductPage()
        {
            try
            {
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"shopping-cart-link\"]")).ToBeVisibleAsync();
                await this.pageObject.Page.Locator("[data-test=\"shopping-cart-link\"]").ClickAsync(new()
                {
                    Timeout = 10_000
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [Then(@"Cart page is loaded")]
        public async Task ThenCartPageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.WaitForURLAsync("**/cart.html", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 10_000
                });
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"secondary-header\"]")).ToBeVisibleAsync();
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"title\"]")).ToContainTextAsync("Your Cart", new()
                {
                    IgnoreCase = false //case sensitive
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Remove a (.*) from cart")]
        public async Task WhenRemoveProductNameFromCart(string productName)
        {
            try
            {
                await this.pageObject.Page.Locator($"xpath=//div[contains(text(),'{productName}')]/ancestor::div[@class='cart_item_label']//button", new()
                {
                    HasText = "Remove"
                })
                    .ClickAsync(new()
                {
                    Delay = 3_000
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Click on checkout button in cart page")]
        public async Task WhenClickOnCheckoutButtonInCartPage()
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"checkout\"]").ClickAsync(new()
                {
                    Delay = 2_000,
                    Timeout = 10_000

                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [Then(@"Checkout info page is loaded")]
        public async Task ThenCheckoutInfoPageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.WaitForURLAsync("**/checkout-step-one.html", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 10_000
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Provide user information ([a-z_A-Z]+) and ([a-z_A-Z]+) and ([0-9]+)")]
        public async Task WhenProvideUserInformationFirstNameAndLastNameAndPinCode(string firstName,string lastName,string pinCode)
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"firstName\"]").ClickAsync();
                await this.pageObject.Page.Locator("[data-test=\"firstName\"]").FillAsync(firstName);
                await this.pageObject.Page.Locator("[data-test=\"lastName\"]").ClickAsync();
                await this.pageObject.Page.Locator("[data-test=\"lastName\"]").FillAsync(lastName);
                await this.pageObject.Page.Locator("[data-test=\"postalCode\"]").ClickAsync();
                await this.pageObject.Page.Locator("[data-test=\"postalCode\"]").FillAsync(pinCode);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Click on continue button in checkout info page")]
        public async Task WhenClickOnContinueButtonInCheckoutInfoPage()
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"continue\"]", new()
                {
                    HasText = "Continue"
                })
                    .ClickAsync(new()
                {
                    Delay = 2_000
                    
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [Then(@"Payment page is loaded")]
        public async Task ThenPaymentPageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.WaitForURLAsync("**/checkout-step-two.html", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 10_000
                });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [When(@"Click on finish button in payment page")]
        public async Task WhenClickOnFinishButtonInPaymentPage()
        {
            try
            {
                await this.pageObject.Page.Locator("[data-test=\"finish\"]", new()
                {                 
                    HasText = "Finish"
                })
                    .ClickAsync(new()
                    {
                        Timeout = 15_000
                    });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        [Then(@"Checkout complete page is loaded")]
        public async Task ThenCheckoutCompletePageIsLoaded()
        {
            try
            {
                await this.pageObject.Page.WaitForURLAsync("**/checkout-complete.html", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 20_000
                });
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"pony-express\"]")).ToBeVisibleAsync();
                await Assertions.Expect(this.pageObject.Page.Locator("[data-test=\"complete-header\"]")).ToContainTextAsync("Thank you for your order!");
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

    }
}