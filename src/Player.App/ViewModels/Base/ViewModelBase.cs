using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Player.App.ViewModels.Base;

/// <summary>
/// The INotifyPropertyChanged plumbing every view model repeated. Properties are still written as
/// <c>public string X { get; private set =&gt; Set(ref field, value); }</c>. Set is protected and generic, so the
/// Qt bridge does not see it as a setter.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged 
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}