using System;
using System.Threading.Tasks;
using System.Windows;
using DMADC.Services;

using MessageBox = System.Windows.MessageBox;

namespace DMADC.Views;

public partial class PasswordDialog : Window
{
    private readonly IPasswordService _pwd = App.Services.Password;

    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += PasswordDialog_Loaded;
    }

    private async void PasswordDialog_Loaded(object sender, RoutedEventArgs e)
    {
        bool firstTime = await _pwd.IsFirstTimeSetupAsync();
        OldPwdLabel.Text = firstTime ? "（首次设置）当前密码留空即可" : "当前密码：";
        if (firstTime) OldPwd.IsEnabled = false;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string oldPwd = OldPwd.Password;
            string newPwd = NewPwd.Password;
            string confirm = ConfirmPwd.Password;

            if (newPwd != confirm)
            {
                MessageBox.Show("两次输入的新密码不一致", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(newPwd) || newPwd.Length < 4)
            {
                MessageBox.Show("新密码至少 4 位", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await _pwd.ChangePasswordAsync(oldPwd, newPwd);
            MessageBox.Show("密码已更新", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("修改失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
