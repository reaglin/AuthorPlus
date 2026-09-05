using System.Windows;
using System.Windows.Controls;
using AuthorPlus.AI;

namespace AuthorPlus.App;

/// <summary>
/// Provider / model / key per provider, the PreseMaker key-sharing opt-in, and a live
/// connection test. Edits are held in a working copy and written only on Save.
/// </summary>
public partial class AiSettingsWindow : Window
{
    private readonly AiSettingsStore _store;
    private readonly AiSettings _working;
    private bool _loading;

    public AiSettingsWindow(AiSettingsStore store)
    {
        InitializeComponent();
        _store   = store;
        _working = store.Load();

        _loading = true;
        foreach (var p in AiProviderRouter.AllProviders)
            CmbProvider.Items.Add(new ComboBoxItem { Content = AiProviderRouter.DisplayName(p), Tag = p });
        CmbProvider.SelectedIndex = Math.Max(0, AiProviderRouter.AllProviders.ToList().IndexOf(_working.SelectedProvider));
        ChkPreseMaker.IsChecked = _working.UsePreseMakerKeys;
        ChkPreseMaker.IsEnabled = PreseMakerCredentials.IsInstalled;
        _loading = false;

        ShowProvider();
        UpdatePreseMakerHint();
    }

    private AiProviderType Selected =>
        (CmbProvider.SelectedItem as ComboBoxItem)?.Tag is AiProviderType p ? p : AiProviderType.Claude;

    private void ShowProvider()
    {
        _loading = true;
        var p = Selected;
        CmbModel.Items.Clear();
        foreach (var m in AiProviderRouter.AvailableModels[p]) CmbModel.Items.Add(m);
        CmbModel.Text = _working.ModelFor(p);

        var key = _working.KeyFor(p);
        PwdKey.Password = key;
        TxtKeyVisible.Text = key;
        TxtTest.Text = string.Empty;
        _loading = false;
        UpdateKeyHint();
    }

    private void UpdateKeyHint()
    {
        var p = Selected;
        if (!string.IsNullOrWhiteSpace(_working.KeyFor(p)))
        {
            TxtKeyHint.Text = "Key entered here (stored encrypted).";
            return;
        }
        var pm = _working.UsePreseMakerKeys ? PreseMakerCredentials.TryLoad() : null;
        TxtKeyHint.Text = pm != null && pm.HasKey(p)
            ? "No key here — the key saved in PreseMaker will be used."
            : "No key. Paste the provider's API key above.";
    }

    private void UpdatePreseMakerHint()
    {
        if (!PreseMakerCredentials.IsInstalled)
        {
            TxtPreseMaker.Text = "PreseMaker settings were not found on this computer.";
            return;
        }
        var pm = PreseMakerCredentials.TryLoad();
        var list = pm?.ProvidersWithKeys ?? Array.Empty<AiProviderType>();
        TxtPreseMaker.Text = list.Count == 0
            ? "PreseMaker is installed but has no API keys saved."
            : "PreseMaker has keys for: " + string.Join(", ", list.Select(AiProviderRouter.DisplayName)) + ". Read-only; never copied to disk here.";
    }

    private void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _working.SelectedProvider = Selected;
        ShowProvider();
    }

    private void PwdKey_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _working.SetKey(Selected, PwdKey.Password.Trim());
        UpdateKeyHint();
    }

    private void TxtKeyVisible_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _working.SetKey(Selected, TxtKeyVisible.Text.Trim());
        UpdateKeyHint();
    }

    private void BtnShow_Click(object sender, RoutedEventArgs e)
    {
        bool showing = TxtKeyVisible.Visibility == Visibility.Visible;
        _loading = true;
        if (showing) { PwdKey.Password = TxtKeyVisible.Text; }
        else          { TxtKeyVisible.Text = PwdKey.Password; }
        _loading = false;
        TxtKeyVisible.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
        PwdKey.Visibility        = showing ? Visibility.Visible   : Visibility.Collapsed;
        BtnShow.Content          = showing ? "Show" : "Hide";
    }

    private void ChkPreseMaker_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _working.UsePreseMakerKeys = ChkPreseMaker.IsChecked == true;
        UpdateKeyHint();
    }

    private void CommitModel() => _working.SetModel(Selected, string.IsNullOrWhiteSpace(CmbModel.Text)
        ? AiProviderRouter.DefaultModels[Selected] : CmbModel.Text.Trim());

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        CommitModel();
        BtnTest.IsEnabled = false;
        TxtTest.Text = "Testing…";
        try
        {
            var effective = PreseMakerCredentials.CreateEffectiveSettings(_working);
            var provider  = AiProviderRouter.GetProvider(effective);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var reply = await provider.GenerateAsync("Reply with the single word OK.", "Ping", cts.Token, maxTokens: 20);
            TxtTest.Text = $"✓ {provider.ProviderName} ({provider.Model}) answered: {reply.Trim()}";
        }
        catch (Exception ex)
        {
            TxtTest.Text = "✗ " + ex.Message;
        }
        finally { BtnTest.IsEnabled = true; }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitModel();
        try
        {
            _store.Save(_working);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings:\n\n{ex.Message}", "AI Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
