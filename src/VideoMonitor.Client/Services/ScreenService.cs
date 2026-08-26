namespace VideoMonitor.Client.Services;

public sealed class ScreenService
{
    public bool HasSecondaryScreen => Screen.AllScreens.Length >= 2;

    public static Rectangle CalculateSecondaryBounds(IReadOnlyList<Rectangle> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);

        if (workingAreas.Count == 0)
        {
            throw new ArgumentException("至少需要一个显示器工作区。", nameof(workingAreas));
        }

        if (workingAreas.Count >= 2)
        {
            var secondary = workingAreas[1];
            return new Rectangle(secondary.Left, secondary.Top, secondary.Width, 540);
        }

        var primary = workingAreas[0];
        var testWidth = Math.Min(1440, Math.Max(900, primary.Width - 160));
        return new Rectangle(primary.Left + 80, primary.Top + 80, testWidth, 540);
    }

    public void ConfigureSecondaryWindow(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var screens = Screen.AllScreens;
        form.StartPosition = FormStartPosition.Manual;
        form.FormBorderStyle = screens.Length >= 2
            ? FormBorderStyle.None
            : FormBorderStyle.Sizable;
        form.Bounds = CalculateSecondaryBounds(
            screens.Select(screen => screen.WorkingArea).ToArray());
        form.MinimumSize = new Size(Math.Min(900, form.Width), 540);
        form.MaximumSize = new Size(int.MaxValue, 540);
    }
}
