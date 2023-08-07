using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Steam_Desktop_Authenticator
{
    public partial class LoginForm : Form
    {
        public SteamGuardAccount account;
        public LoginType LoginReason;
        public SessionData Session;

        public LoginForm(LoginType loginReason = LoginType.Initial, SteamGuardAccount account = null)
        {
            InitializeComponent();
            this.LoginReason = loginReason;
            this.account = account;

            try
            {
                if (this.LoginReason != LoginType.Initial)
                {
                    txtUsername.Text = account.AccountName;
                    txtUsername.Enabled = false;
                }

                if (this.LoginReason == LoginType.Refresh)
                {
                    labelLoginExplanation.Text = "你的steam令牌过期了。为了保障交易确认和市场确认正常工作，请重新登录。";
                }
                else if (this.LoginReason == LoginType.Import)
                {
                    labelLoginExplanation.Text = "请登录您的Steam帐户导入它。";
                }
            }
            catch (Exception)
            {
                MessageBox.Show("无法找到账户。请关闭并重新打开SDA尝试。", "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        public void SetUsername(string username)
        {
            txtUsername.Text = username;
        }

        public string FilterPhoneNumber(string phoneNumber)
        {
            return phoneNumber.Replace("-", "").Replace("(", "").Replace(")", "");
        }

        public bool PhoneNumberOkay(string phoneNumber)
        {
            if (phoneNumber == null || phoneNumber.Length == 0) return false;
            if (phoneNumber[0] != '+') return false;
            return true;
        }

        private void ResetLoginButton()
        {
            btnSteamLogin.Enabled = true;
            btnSteamLogin.Text = "Login";
        }

        private async void btnSteamLogin_Click(object sender, EventArgs e)
        {
            // Disable button while we login
            btnSteamLogin.Enabled = false;
            btnSteamLogin.Text = "正在登录...";

            string username = txtUsername.Text;
            string password = txtPassword.Text;

            // Start a new SteamClient instance
            SteamClient steamClient = new SteamClient();

            // Connect to Steam
            steamClient.Connect();

            // Really basic way to wait until Steam is connected
            while (!steamClient.IsConnected)
                await Task.Delay(500);

            // Create a new auth session
            CredentialsAuthSession authSession;
            try
            {
                authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new UserFormAuthenticator(this.account),
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Steam登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Starting polling Steam for authentication response
            AuthPollResult pollResponse;
            try
            {
                pollResponse = await authSession.PollingWaitForResultAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Steam登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Build a SessionData object
            SessionData sessionData = new SessionData()
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = pollResponse.AccessToken,
                RefreshToken = pollResponse.RefreshToken,
            };

            //Login succeeded
            this.Session = sessionData;

            // If we're only logging in for an account import, stop here
            if (LoginReason == LoginType.Import)
            {
                this.Close();
                return;
            }

            // If we're only logging in for a session refresh then save it and exit
            if (LoginReason == LoginType.Refresh)
            {
                Manifest man = Manifest.GetManifest();
                account.FullyEnrolled = true;
                account.Session = sessionData;
                HandleManifest(man, true);
                this.Close();
                return;
            }

            // Show a dialog to make sure they really want to add their authenticator
            var result = MessageBox.Show("Steam帐户登录成功。按“确定”继续添加SDA作为您的验证器。", "登录Steam", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (result == DialogResult.Cancel)
            {
                MessageBox.Show("添加验证器已中止。", "登录Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetLoginButton();
                return;
            }

            // Begin linking mobile authenticator
            AuthenticatorLinker linker = new AuthenticatorLinker(sessionData);

            AuthenticatorLinker.LinkResult linkResponse = AuthenticatorLinker.LinkResult.GeneralFailure;
            while (linkResponse != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            {
                try
                {
                    linkResponse = await linker.AddAuthenticator();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("添加验证器时出错: " + ex.Message, "登录Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetLoginButton();
                    return;
                }

                switch (linkResponse)
                {
                    case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
                        string phoneNumber = "";
                        while (!PhoneNumberOkay(phoneNumber))
                        {
                            InputForm phoneNumberForm = new InputForm("以下列格式输入您的电话号码：+{区号}电话号码。+86 12345678910");
                            phoneNumberForm.txtBox.Text = "+86 ";
                            phoneNumberForm.ShowDialog();
                            if (phoneNumberForm.Canceled)
                            {
                                this.Close();
                                return;
                            }

                            phoneNumber = FilterPhoneNumber(phoneNumberForm.txtBox.Text);
                        }
                        linker.PhoneNumber = phoneNumber;
                        break;

                    case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                        MessageBox.Show("此帐户已链接了身份验证程序。必须删除该验证器才能将SDA添加为验证器。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;

                    case AuthenticatorLinker.LinkResult.FailureAddingPhone:
                        MessageBox.Show("无法添加您的电话号码。请重试或使用其他电话号码。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustRemovePhoneNumber:
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                        MessageBox.Show("请检查您的电子邮件，并单击Steam发送给您的链接，然后继续。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;

                    case AuthenticatorLinker.LinkResult.GeneralFailure:
                        MessageBox.Show("添加验证器时出错。", "Steam 登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;
                }
            } // End while loop checking for AwaitingFinalization

            Manifest manifest = Manifest.GetManifest();
            string passKey = null;
            if (manifest.Entries.Count == 0)
            {
                passKey = manifest.PromptSetupPassKey("请输入加密密钥。留空或点击取消不加密（非常不安全）。");
            }
            else if (manifest.Entries.Count > 0 && manifest.Encrypted)
            {
                bool passKeyValid = false;
                while (!passKeyValid)
                {
                    InputForm passKeyForm = new InputForm("请输入您当前的加密密钥。");
                    passKeyForm.ShowDialog();
                    if (!passKeyForm.Canceled)
                    {
                        passKey = passKeyForm.txtBox.Text;
                        passKeyValid = manifest.VerifyPasskey(passKey);
                        if (!passKeyValid)
                        {
                            MessageBox.Show("该密钥无效。请输入您用于其他帐户的相同密钥。");
                        }
                    }
                    else
                    {
                        this.Close();
                        return;
                    }
                }
            }

            //Save the file immediately; losing this would be bad.
            if (!manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey))
            {
                manifest.RemoveAccount(linker.LinkedAccount);
                MessageBox.Show("无法保存移动验证器文件。移动验证器尚未链接。");
                this.Close();
                return;
            }

            MessageBox.Show("移动身份验证程序尚未链接。在完成身份验证程序之前，请写下您的撤销代码: " + linker.LinkedAccount.RevocationCode);

            AuthenticatorLinker.FinalizeResult finalizeResponse = AuthenticatorLinker.FinalizeResult.GeneralFailure;
            while (finalizeResponse != AuthenticatorLinker.FinalizeResult.Success)
            {
                InputForm smsCodeForm = new InputForm("请输入发送到您手机的短信代码。");
                smsCodeForm.ShowDialog();
                if (smsCodeForm.Canceled)
                {
                    manifest.RemoveAccount(linker.LinkedAccount);
                    this.Close();
                    return;
                }

                InputForm confirmRevocationCode = new InputForm("请输入您的撤销代码以确保已保存。");
                confirmRevocationCode.ShowDialog();
                if (confirmRevocationCode.txtBox.Text.ToUpper() != linker.LinkedAccount.RevocationCode)
                {
                    MessageBox.Show("撤销代码不正确；身份验证器尚未链接。");
                    manifest.RemoveAccount(linker.LinkedAccount);
                    this.Close();
                    return;
                }

                string smsCode = smsCodeForm.txtBox.Text;
                finalizeResponse = await linker.FinalizeAddAuthenticator(smsCode);

                switch (finalizeResponse)
                {
                    case AuthenticatorLinker.FinalizeResult.BadSMSCode:
                        continue;

                    case AuthenticatorLinker.FinalizeResult.UnableToGenerateCorrectCodes:
                        MessageBox.Show("无法生成正确的代码来完成此身份验证程序。不应链接身份验证程序。在不太可能的情况下，请写下你的撤销代码，因为这是最后一次看到它的机会: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        this.Close();
                        return;

                    case AuthenticatorLinker.FinalizeResult.GeneralFailure:
                        MessageBox.Show("无法完成此身份验证程序。不应链接身份验证程序。在不太可能的情况下，请写下你的撤销代码，因为这是最后一次看到它的机会: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        this.Close();
                        return;
                }
            }

            //Linked, finally. Re-save with FullyEnrolled property.
            manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey);
            MessageBox.Show("移动验证器已成功链接。请写下您的撤销代码: " + linker.LinkedAccount.RevocationCode);
            this.Close();
        }

        private void HandleManifest(Manifest man, bool IsRefreshing = false)
        {
            string passKey = null;
            if (man.Entries.Count == 0)
            {
                passKey = man.PromptSetupPassKey("请输入加密密钥。留空或点击取消不加密（非常不安全）。");
            }
            else if (man.Entries.Count > 0 && man.Encrypted)
            {
                bool passKeyValid = false;
                while (!passKeyValid)
                {
                    InputForm passKeyForm = new InputForm("请输入您当前的加密密钥。");
                    passKeyForm.ShowDialog();
                    if (!passKeyForm.Canceled)
                    {
                        passKey = passKeyForm.txtBox.Text;
                        passKeyValid = man.VerifyPasskey(passKey);
                        if (!passKeyValid)
                        {
                            MessageBox.Show("该密钥无效。请输入您用于其他帐户的相同密钥。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        this.Close();
                        return;
                    }
                }
            }

            man.SaveAccount(account, passKey != null, passKey);
            if (IsRefreshing)
            {
                MessageBox.Show("您的会话已刷新。", "Steam登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("移动验证器已成功链接。请写下您的撤销代码: " + account.RevocationCode, "Steam登录", MessageBoxButtons.OK);
            }
            this.Close();
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            if (account != null && account.AccountName != null)
            {
                txtUsername.Text = account.AccountName;
            }
        }

        public enum LoginType
        {
            Initial,
            Refresh,
            Import
        }
    }
}
