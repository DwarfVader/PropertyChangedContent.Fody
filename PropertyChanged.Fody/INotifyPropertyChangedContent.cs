using System.ComponentModel;

/// <summary>
/// A marker interface to indicate that internal changes of properties shall fire
/// PropertyChanged event handler of outer property. 
/// Works as follows: class.outerProperty.innerProperty fires event -> class.outerProperty fires event
/// Outer property registers its event handler to inner property's event to get fired upon 
/// inner changes. 
/// </summary>
public interface INotifyPropertyChangedContent : INotifyPropertyChanged { }