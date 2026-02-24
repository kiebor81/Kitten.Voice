using Avalonia.Controls;
using Kitten.Voice;
using System.Globalization;
using System.Security;

namespace Kitten.Voice.UI;

public partial class MainWindow : Window
{
    private readonly Speaker _speaker;
    private readonly SemaphoreSlim _speakLock = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();

        string assetsPath = Path.Combine(AppContext.BaseDirectory, "assets");
        _speaker = new Speaker(assetsPath) { Output = AudioOutput.Stream };

        string[] voices = LoadVoiceNames(assetsPath);
        VoiceComboBox.ItemsSource = voices;
        BuilderVoiceComboBox.ItemsSource = voices;

        VoiceComboBox.SelectedItem = voices.FirstOrDefault() ?? "Bella";
        BuilderVoiceComboBox.SelectedItem = voices.FirstOrDefault() ?? "Bella";
        PlainSpeedComboBox.ItemsSource = new[] { "0.8", "1.0", "1.2", "1.3", "1.5", "1.8" };
        PlainSpeedComboBox.SelectedItem = "1.3";

        EmotionComboBox.ItemsSource = new[] { "none", "happy", "excited", "sad", "angry", "calm", "fearful" };
        EmotionComboBox.SelectedItem = "none";

        IntensityComboBox.ItemsSource = new[] { "weak", "medium", "strong", "x-strong", "0.8", "1.0", "1.2" };
        IntensityComboBox.SelectedItem = "medium";

        RateComboBox.ItemsSource = new[] { "x-slow", "slow", "medium", "fast", "x-fast" };
        RateComboBox.SelectedItem = "medium";

        PitchComboBox.ItemsSource = new[] { "x-low", "low", "medium", "high", "x-high", "-2st", "+2st" };
        PitchComboBox.SelectedItem = "medium";

        VolumeComboBox.ItemsSource = new[] { "x-soft", "soft", "medium", "loud", "x-loud", "90%", "110%" };
        VolumeComboBox.SelectedItem = "medium";

        BuilderTextBox.Text = "This is a quick SSML builder test for Kitten Voice.";
        SsmlPreviewTextBox.Text = BuildSsmlFromBuilder();
    }

    private static string[] LoadVoiceNames(string assetsPath)
    {
        try
        {
            return Speaker.GetAvailableVoices(assetsPath);
        }
        catch
        {
            return ["Bella", "Bruno", "Hugo", "Jasper", "Kiki", "Leo", "Luna", "Rosie"];
        }
    }

    private async void PlayTextButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string text = (PlainTextBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            SetStatus("Text input is empty.");
            return;
        }

        string voice = (VoiceComboBox.SelectedItem as string) ?? "Bella";
        float speed = GetSelectedPlainSpeed();
        await SpeakAsync(text, voice, "Text played.", speed);
    }

    private void GenerateSsmlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SsmlPreviewTextBox.Text = BuildSsmlFromBuilder();
        SetStatus("SSML generated.");
    }

    private async void PlaySsmlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string ssml = BuildSsmlFromBuilder();
        SsmlPreviewTextBox.Text = ssml;

        string voice = (BuilderVoiceComboBox.SelectedItem as string) ?? "Bella";
        await SpeakAsync(ssml, voice, "SSML played.");
    }

    private string BuildSsmlFromBuilder()
    {
        string text = (BuilderTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string voice = (BuilderVoiceComboBox.SelectedItem as string) ?? "Bella";
        string emotion = (EmotionComboBox.SelectedItem as string) ?? "none";
        string intensity = (IntensityComboBox.SelectedItem as string) ?? "medium";
        string rate = (RateComboBox.SelectedItem as string) ?? "medium";
        string pitch = (PitchComboBox.SelectedItem as string) ?? "medium";
        string volume = (VolumeComboBox.SelectedItem as string) ?? "medium";

        string escaped = SecurityElement.Escape(text) ?? text;
        string content = escaped;

        if (!string.Equals(emotion, "none", StringComparison.OrdinalIgnoreCase))
            content = $"<emotion name=\"{emotion}\" intensity=\"{intensity}\">{content}</emotion>";

        if (rate != "medium" || pitch != "medium" || volume != "medium")
            content = $"<prosody rate=\"{rate}\" pitch=\"{pitch}\" volume=\"{volume}\">{content}</prosody>";

        return $"<speak><voice name=\"{voice}\">{content}</voice></speak>";
    }

    private float GetSelectedPlainSpeed()
    {
        if (PlainSpeedComboBox.SelectedItem is string speedText
            && float.TryParse(speedText, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)
            && speed > 0f)
        {
            return speed;
        }

        return _speaker.Speed;
    }

    private async Task SpeakAsync(string input, string voice, string successMessage, float? speedOverride = null)
    {
        float effectiveSpeed = speedOverride ?? _speaker.Speed;
        SetStatus($"Speaking as {voice} ({effectiveSpeed:0.##}x)...");

        await _speakLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                float savedSpeed = _speaker.Speed;
                _speaker.Voice = voice;
                try
                {
                    if (speedOverride.HasValue)
                        _speaker.Speed = speedOverride.Value;

                    _speaker.Say(input);
                }
                finally
                {
                    _speaker.Speed = savedSpeed;
                }
            });
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _speakLock.Release();
        }
    }

    private void SetStatus(string text) => StatusTextBlock.Text = text;
}
