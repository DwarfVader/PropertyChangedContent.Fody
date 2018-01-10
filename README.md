# PropertyChangedContent.Fody
This is an extension to "PropertyChanged.Fody". 
If a (child) property implements "INotifyPropertyChanged" and the containing class (parent object) implements "INotifyPropertyChangedContent", the parent object registers itself to its child property's PropertyChanged event such that the parent gets notified (and fires its own PropertyChanged event) as the content of its child property changes. 
If the constraints for content changed notification are not satisfied, it acts like the common "PropertyChanged.Fody". 

Although ObservacleCollection implements "INotifyPropertyChanged", it gets ignored by "PropertyChangedContent.Fody". 

In order to use it, add "PropertyChangedContent" to the weavers in "FodyWeavers.xml":
//<?xml version="1.0" encoding="utf-8"?>
//<Weavers>
//  <PropertyChangedContent/>
//</Weavers>
