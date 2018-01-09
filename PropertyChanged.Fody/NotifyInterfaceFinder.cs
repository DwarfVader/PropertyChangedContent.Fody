using System.Collections.Generic;
using Mono.Cecil;

public partial class ModuleWeaver
{
    Dictionary<string, bool> typesImplementingINotify = new Dictionary<string, bool>();
    Dictionary<string, bool> typesImplementingINotifyContent = new Dictionary<string, bool>();

    public bool HierarchyImplementsINotify (TypeReference typeReference)
    {
        var fullName = typeReference.FullName;
        if (typesImplementingINotify.TryGetValue(fullName, out var implementsINotify))
        {
            return implementsINotify;
        }

        TypeDefinition typeDefinition;
        if (typeReference.IsDefinition)
        {
            typeDefinition = (TypeDefinition)typeReference;
        }
        else
        {
            typeDefinition = Resolve(typeReference);
        }

        foreach (var interfaceImplementation in typeDefinition.Interfaces)
        {
            if (interfaceImplementation.InterfaceType.Name == "INotifyPropertyChanged")
            {
                typesImplementingINotify[fullName] = true;
                return true;
            }
        }

        var baseType = typeDefinition.BaseType;
        if (baseType == null)
        {
            typesImplementingINotify[fullName] = false;
            return false;
        }

        var baseTypeImplementsINotify = HierarchyImplementsINotify(baseType);
        typesImplementingINotify[fullName] = baseTypeImplementsINotify;
        return baseTypeImplementsINotify;
    }

    public bool HierarchyImplementsINotifyContent (TypeReference typeReference)
    {
        //first, check dictionary if information about current type is available
        var fullName = typeReference.FullName;
        if (typesImplementingINotifyContent.TryGetValue(fullName, out var implementsINotifyContent))
        {
            //information found
            return implementsINotifyContent;
        }

        //not found in dictionary -> perform check on current type
        TypeDefinition typeDefinition;
        if (typeReference.IsDefinition)
        {
            typeDefinition = (TypeDefinition)typeReference;
        }
        else
        {
            typeDefinition = Resolve(typeReference);
        }

        //loop over interfaces of current type
        foreach (var interfaceImplementation in typeDefinition.Interfaces)
        {
            //current type implements the interface
            if (interfaceImplementation.InterfaceType.Name == "INotifyPropertyChangedContent")
            {
                typesImplementingINotifyContent[fullName] = true;
                return true;
            }
        }

        //check base type
        var baseType = typeDefinition.BaseType;
        //no base type...
        if (baseType == null)
        {
            //current type does not implement the interface
            typesImplementingINotifyContent[fullName] = false;
            return false;
        }

        //base type exists -> perform recursive check
        var baseTypeImplementsINotifyContent = HierarchyImplementsINotifyContent(baseType);
        typesImplementingINotifyContent[fullName] = baseTypeImplementsINotifyContent;
        return baseTypeImplementsINotifyContent;
    }
}