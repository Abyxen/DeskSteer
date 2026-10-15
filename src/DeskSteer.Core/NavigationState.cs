using System.ComponentModel;

namespace DeskSteer.Core;

public enum Page { Home, Connect, Host, Devices, Settings, About }

public sealed class NavigationState : INotifyPropertyChanged
{
    public Page Current { get; private set; } = Page.Home;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Navigate(Page page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        if (Current == page) return;
        Current = page;
        PropertyChanged?.Invoke(this, new(nameof(Current)));
    }
}
