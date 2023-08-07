using SteamAuth;
using SteamKit2.Authentication;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal class UserFormAuthenticator : IAuthenticator
    {
        private SteamGuardAccount account;
        private int deviceCodesGenerated = 0;

        public UserFormAuthenticator(SteamGuardAccount account)
        {
            this.account = account;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            // If a code fails wait 30 seconds for a new one to regenerate
            if (previousCodeWasIncorrect)
            {
                // After 2 tries tell the user that there seems to be an issue
                if (deviceCodesGenerated > 2)
                    MessageBox.Show("使用这两个因素代码登录您的帐户似乎有问题。你确定SDA仍然是你的验证器吗？");

                await Task.Delay(30000);
            }

            string deviceCode = await account.GenerateSteamGuardCodeAsync();
            deviceCodesGenerated++;

            return deviceCode;
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            string message = "输入发送到您的电子邮件的代码:";
            if (previousCodeWasIncorrect)
            {
                message = "您提供的代码无效。输入发送到您的电子邮件的代码:";
            }

            InputForm emailForm = new InputForm(message);
            emailForm.ShowDialog();
            return Task.FromResult(emailForm.txtBox.Text);
        }
    }
}
