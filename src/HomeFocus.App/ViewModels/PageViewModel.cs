using CommunityToolkit.Mvvm.ComponentModel;

namespace HomeFocus.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    /// <summary>Reload everything from the database.</summary>
    public abstract void Refresh();

    /// <summary>Called every few seconds while the page is visible.</summary>
    public virtual void OnTimer() { }
}
