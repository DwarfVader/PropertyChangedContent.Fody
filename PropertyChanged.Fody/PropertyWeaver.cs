using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

public class PropertyWeaver
{
    ModuleWeaver moduleWeaver;
    PropertyData propertyData;
    TypeNode typeNode;
    TypeSystem typeSystem;
    MethodBody setMethodBody;
    Collection<Instruction> instructions;

    //SEPPL: method called by child property's on changed event
    MethodDefinition delegateMethod;

    public PropertyWeaver (ModuleWeaver moduleWeaver, PropertyData propertyData, TypeNode typeNode, TypeSystem typeSystem)
    {
        this.moduleWeaver = moduleWeaver;
        this.propertyData = propertyData;
        this.typeNode = typeNode;
        this.typeSystem = typeSystem;
    }

    public void Execute ()
    {
        moduleWeaver.LogDebug("\t\t" + propertyData.PropertyDefinition.Name);
        var property = propertyData.PropertyDefinition;
        setMethodBody = property.SetMethod.Body;
        instructions = property.SetMethod.Body.Instructions;

        //SEPPL: create delegate method needed to register parent object to child property
        CreateDelegateMethod();

        var indexes = GetIndexes();
        indexes.Reverse();
        foreach (var index in indexes)
        {
            InjectAtIndex(index);
        }

        //SEPPL: register parent object to new child property
        //insert code after backing field assignment index
        RegisterParentToChild(indexes[0]+1);

        //SEPPL: unregister parent object from old child property
        //insert code before backingfield assignment index
        UnregisterParentFromChild(indexes[0]-1);
    }

    /// <summary>
    /// SEPPL: 
    /// Registers an observable object's OnPropertyChanged() to the porperty changed event of its child property
    /// </summary>
    void RegisterParentToChild (int index)
    {

        //TODO: adapt for NotifyCollectionChangedEventHandler

        //only register to types that implement the INotifyPropertyChanged interface if the 
        //containing class implements INotifyPropertyChangedContent and 
        //property offers a public event handler to register to
        if (moduleWeaver.HierarchyImplementsINotifyContent(propertyData.PropertyDefinition.DeclaringType) &&
            moduleWeaver.HierarchyImplementsINotify(propertyData.PropertyDefinition.PropertyType) &&
            FindBaseTypeWithPublicEvent(propertyData.PropertyDefinition.PropertyType.Resolve(), "PropertyChangedEventHandler") != null)
        {

            //add handler method of property's changed event
            MethodReference eventMethod = FindMethodInHierarchy(
                    propertyData.PropertyDefinition.PropertyType.Resolve(), "add_PropertyChanged");

            //TODO: store this somewhere globally
            TypeDefinition propChangedEventType = moduleWeaver.ModuleDefinition.ImportReference(
                typeof(System.ComponentModel.PropertyChangedEventHandler)).Resolve();

            //constructor for property changed event handler
            MethodReference ctor = moduleWeaver.ModuleDefinition.ImportReference(
                propChangedEventType.Methods.First(m => m.IsConstructor));

            //get final instruction as branch target
            Instruction finalInstruction = Instruction.Create(OpCodes.Nop);

            //delegate instructions
            instructions.Insert(index,
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldfld, propertyData.BackingFieldReference),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldftn, delegateMethod),
                 Instruction.Create(OpCodes.Newobj, ctor),
                 Instruction.Create(OpCodes.Callvirt, eventMethod),
                 finalInstruction);

            //instructions to branch if value == null; insert before delegate instructions
            instructions.Insert(index,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, propertyData.BackingFieldReference),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Call, moduleWeaver.ModuleDefinition.ImportReference(moduleWeaver.ModuleDefinition.ImportReference(
                    typeof(System.Object)).Resolve().Methods.First(m => m.Name.Equals("Equals") && m.Parameters.Count == 2))),
                Instruction.Create(OpCodes.Brtrue, finalInstruction));
            //TODO: store reference to object.equals somewhere...
        }
    }

    /// <summary>
    /// SEPPL: Reverts the <see cref="RegisterParentToChild"/>
    /// </summary>
    void UnregisterParentFromChild (int index)
    {
        //only unregister from types that implement the INotifyPropertyChanged interface if the 
        //containing class implements INotifyPropertyChangedContent
        if (moduleWeaver.HierarchyImplementsINotifyContent(propertyData.PropertyDefinition.DeclaringType) &&
            moduleWeaver.HierarchyImplementsINotify(propertyData.PropertyDefinition.PropertyType) &&
            FindBaseTypeWithPublicEvent(propertyData.PropertyDefinition.PropertyType.Resolve(), "PropertyChangedEventHandler") != null)
        {
            //add handler method of property's changed event
            MethodReference eventMethod = FindMethodInHierarchy(
                    propertyData.PropertyDefinition.PropertyType.Resolve(), "remove_PropertyChanged");

            //TODO: store this somewhere globally
            TypeDefinition propChangedEventType = moduleWeaver.ModuleDefinition.ImportReference(
                typeof(System.ComponentModel.PropertyChangedEventHandler)).Resolve();

            //constructor for property changed event handler
            MethodReference ctor = moduleWeaver.ModuleDefinition.ImportReference(
                propChangedEventType.Methods.First(m => m.IsConstructor));

            //get final instruction as branch target
            Instruction finalInstruction = Instruction.Create(OpCodes.Nop);

            //delegate instructions
            instructions.Insert(index,
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldfld, propertyData.BackingFieldReference),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldftn, delegateMethod),
                 Instruction.Create(OpCodes.Newobj, ctor),
                 Instruction.Create(OpCodes.Callvirt, eventMethod),
                 finalInstruction);

            //instructions to branch if value == null; insert before delegate instructions
            instructions.Insert(index,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, propertyData.BackingFieldReference),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Call, moduleWeaver.ModuleDefinition.ImportReference(moduleWeaver.ModuleDefinition.ImportReference(
                    typeof(System.Object)).Resolve().Methods.First(m => m.Name.Equals("Equals") && m.Parameters.Count == 2))),
                Instruction.Create(OpCodes.Brtrue, finalInstruction));
            //TODO: store reference to object.equals somewhere...
        }
    }

    /// <summary>
    /// SEPPL: 
    /// Create the method to be called by a property's on changed event
    /// </summary>
    void CreateDelegateMethod ()
    {
        //only create delegate method if parent of current property is going to register itself
        if (!moduleWeaver.HierarchyImplementsINotify(propertyData.PropertyDefinition.PropertyType))
            return;

        var delegateMethod = new MethodDefinition("<set_"+ propertyData.PropertyDefinition.Name + ">delegateMethod",
        MethodAttributes.Private | MethodAttributes.HideBySig, typeSystem.Void);
        delegateMethod.Parameters.Add(new ParameterDefinition("s", ParameterAttributes.None, typeSystem.Object));
        delegateMethod.Parameters.Add(new ParameterDefinition("e", ParameterAttributes.None, moduleWeaver.ModuleDefinition.ImportReference(typeof(System.ComponentModel.PropertyChangedEventArgs))));
        //delegateMethod.SemanticsAttributes = MethodSemanticsAttributes.NONE;
        //delegateMethod.CustomAttributes.Add(new CustomAttribute(msCoreReferenceFinder.CompilerGeneratedReference));
        var delInstructions = delegateMethod.Body.Instructions;

        //call OnPropertyChanged method of currently considered property
        /*var propertyDefinitions = propertyData.AlsoNotifyFor.Distinct();
        index = propertyDefinitions.Aggregate(index, AddEventInvokeCall);
        AddEventInvokeCall(index, propertyData.PropertyDefinition);*/

        foreach (var item in propertyData.AlsoNotifyFor.Distinct())
        {
            delInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            delInstructions.Add(Instruction.Create(OpCodes.Ldstr, item.Name));
            delInstructions.Add(CallEventInvoker(propertyData.PropertyDefinition));
        }

        delInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        delInstructions.Add(Instruction.Create(OpCodes.Ldstr, propertyData.PropertyDefinition.Name));
        delInstructions.Add(CallEventInvoker(propertyData.PropertyDefinition));

        delInstructions.Add(Instruction.Create(OpCodes.Nop));
        delInstructions.Add(Instruction.Create(OpCodes.Ret));

        this.delegateMethod = delegateMethod;
        typeNode.TypeDefinition.Methods.Add(delegateMethod);
    }

    #region SEPPL: helper methods

    TypeDefinition FindBaseTypeWithMethod (TypeDefinition type, string name)
    {
        //method found in type
        if (type.Methods.Any(m => m.Name == name))
            return type;
        //not found in type; recurse over parent
        else if (type.BaseType != null)
            return FindBaseTypeWithMethod(type.BaseType.Resolve(), name);
        //no base type; method not available
        else
            return null;
    }
    MethodReference FindMethodInHierarchy (TypeDefinition type, string name)
    {
        TypeDefinition containingType = FindBaseTypeWithMethod(type, name);
        if (containingType != null)
        {
            return moduleWeaver.ModuleDefinition.ImportReference(containingType.Methods.First(m => m.Name == name));
        }
        else
            return null;
    }

    List<string> EventNames (TypeDefinition type)
    {
        List<string> names = new List<string>();

        names.AddRange(type.Events.Where(e => e.AddMethod.IsPublic && e.RemoveMethod.IsPublic).Select(e => e.EventType.Name));

        if (type.BaseType != null)
            names.AddRange(EventNames(type.BaseType.Resolve()));

        return names;
    }

    TypeDefinition FindBaseTypeWithPublicEvent (TypeDefinition type, string name)
    {
        //event found in type
        //TODO: improve: could not find a way to check if event itself is public; had to check its add/remove methods
        if (type.Events.Any(e => e.EventType.Name == name && e.AddMethod.IsPublic && e.RemoveMethod.IsPublic))
            return type;
        //not found in type; recurse over parent
        else if (type.BaseType != null)
            return FindBaseTypeWithPublicEvent(type.BaseType.Resolve(), name);
        //no base type; event not available
        else
            return null;
    }
    EventReference FindPublicEventInHierarchy (TypeDefinition type, string name)
    {
        TypeDefinition containingType = FindBaseTypeWithPublicEvent(type, name);
        if (containingType != null)
        {
            //TODO: improve: could not find a way to check if event itself is public; had to check its add/remove methods
            return containingType.Events.First(e => e.EventType.Name == name && e.AddMethod.IsPublic && e.RemoveMethod.IsPublic);
        }
        else
            return null;
    }

    #endregion

    List<int> GetIndexes ()
    {
        if (propertyData.BackingFieldReference == null)
        {
            return new List<int> { instructions.Count -1 };
        }
        var setFieldInstructions = FindSetFieldInstructions().ToList();
        if (setFieldInstructions.Count == 0)
        {
            return new List<int> { instructions.Count-1 };
        }
        return setFieldInstructions;
    }

    void InjectAtIndex (int index)
    {
        index = AddIsChangedSetterCall(index);
        var propertyDefinitions = propertyData.AlsoNotifyFor.Distinct();

        index = propertyDefinitions.Aggregate(index, AddEventInvokeCall);
        AddEventInvokeCall(index, propertyData.PropertyDefinition);
    }

    IEnumerable<int> FindSetFieldInstructions ()
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            if (instruction.OpCode == OpCodes.Stfld)
            {
                var fieldReference = instruction.Operand as FieldReference;
                if (fieldReference == null)
                {
                    continue;
                }

                if (fieldReference.Name == propertyData.BackingFieldReference.Name)
                {
                    yield return index + 1;
                }
            }
            else if (instruction.OpCode == OpCodes.Ldflda)
            {
                if (instruction.Next==null)
                {
                    continue;
                }
                if (instruction.Next.OpCode!=OpCodes.Initobj)
                {
                    continue;
                }
                var fieldReference = instruction.Operand as FieldReference;
                if (fieldReference == null)
                {
                    continue;
                }

                if (fieldReference.Name == propertyData.BackingFieldReference.Name)
                {
                    yield return index + 2;
                }
            }
        }
    }

    int AddIsChangedSetterCall (int index)
    {
        if (typeNode.IsChangedInvoker != null &&
            !propertyData.PropertyDefinition.CustomAttributes.ContainsAttribute("PropertyChanged.DoNotSetChangedAttribute") &&
            propertyData.PropertyDefinition.Name != "IsChanged")
        {
            moduleWeaver.LogDebug("\t\t\tSet IsChanged");
            return instructions.Insert(index,
                                       Instruction.Create(OpCodes.Ldarg_0),
                                       Instruction.Create(OpCodes.Ldc_I4, 1),
                                       CreateIsChangedInvoker());
        }
        return index;
    }

    int AddEventInvokeCall (int index, PropertyDefinition property)
    {
        index = AddOnChangedMethodCall(index, property);
        if (propertyData.AlreadyNotifies.Contains(property.Name))
        {
            moduleWeaver.LogDebug($"\t\t\t{property.Name} skipped since call already exists");
            return index;
        }

        moduleWeaver.LogDebug($"\t\t\t{property.Name}");
        if (typeNode.EventInvoker.InvokerType == InvokerTypes.BeforeAfterGeneric)
        {
            return AddBeforeAfterGenericInvokerCall(index, property);
        }
        if (typeNode.EventInvoker.InvokerType == InvokerTypes.BeforeAfter)
        {
            return AddBeforeAfterInvokerCall(index, property);
        }
        if (typeNode.EventInvoker.InvokerType == InvokerTypes.PropertyChangedArg)
        {
            return AddPropertyChangedArgInvokerCall(index, property);
        }
        if (typeNode.EventInvoker.InvokerType == InvokerTypes.SenderPropertyChangedArg)
        {
            return AddSenderPropertyChangedArgInvokerCall(index, property);
        }
        return AddSimpleInvokerCall(index, property);
    }

    int AddOnChangedMethodCall (int index, PropertyDefinition property)
    {
        if (!moduleWeaver.InjectOnPropertyNameChanged)
        {
            return index;
        }
        var onChangedMethodName = $"On{property.Name}Changed";
        if (ContainsCallToMethod(onChangedMethodName))
        {
            return index;
        }
        var onChangedMethod = typeNode
            .OnChangedMethods
            .FirstOrDefault(x => x.MethodReference.Name == onChangedMethodName);
        if (onChangedMethod == null)
        {
            return index;
        }

        if (onChangedMethod.OnChangedType == OnChangedTypes.NoArg)
        {
            return AddSimpleOnChangedCall(index, onChangedMethod.MethodReference);
        }

        if (onChangedMethod.OnChangedType == OnChangedTypes.BeforeAfter)
        {
            return AddBeforeAfterOnChangedCall(index, property, onChangedMethod.MethodReference);
        }
        return index;
    }

    bool ContainsCallToMethod (string onChangingMethodName)
    {
        return instructions.Select(x => x.Operand)
            .OfType<MethodReference>()
            .Any(x => x.Name == onChangingMethodName);
    }

    int AddSimpleInvokerCall (int index, PropertyDefinition property)
    {
        return instructions.Insert(index,
                                   Instruction.Create(OpCodes.Ldarg_0),
                                   Instruction.Create(OpCodes.Ldstr, property.Name),
                                   CallEventInvoker(property));
    }

    int AddPropertyChangedArgInvokerCall (int index, PropertyDefinition property)
    {
        return instructions.Insert(index,
                                   Instruction.Create(OpCodes.Ldarg_0),
                                   Instruction.Create(OpCodes.Ldstr, property.Name),
                                   Instruction.Create(OpCodes.Newobj, moduleWeaver.PropertyChangedEventConstructorReference),
                                   CallEventInvoker(property));
    }

    int AddSenderPropertyChangedArgInvokerCall (int index, PropertyDefinition property)
    {
        return instructions.Insert(index,
                                   Instruction.Create(OpCodes.Ldarg_0),
                                   Instruction.Create(OpCodes.Ldarg_0),
                                   Instruction.Create(OpCodes.Ldstr, property.Name),
                                   Instruction.Create(OpCodes.Newobj, moduleWeaver.PropertyChangedEventConstructorReference),
                                   CallEventInvoker(property));
    }

    int AddBeforeAfterGenericInvokerCall (int index, PropertyDefinition property)
    {
        var beforeVariable = new VariableDefinition(property.PropertyType);
        setMethodBody.Variables.Add(beforeVariable);
        var afterVariable = new VariableDefinition(property.PropertyType);
        setMethodBody.Variables.Add(afterVariable);

        index = InsertVariableAssignmentFromCurrentValue(index, property, afterVariable);

        index = instructions.Insert(index,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, property.Name),
            Instruction.Create(OpCodes.Ldloc, beforeVariable),
            Instruction.Create(OpCodes.Ldloc, afterVariable),
            CallEventInvoker(property)
            );

        return AddBeforeVariableAssignment(index, property, beforeVariable);
    }

    int AddBeforeAfterInvokerCall (int index, PropertyDefinition property)
    {
        var beforeVariable = new VariableDefinition(typeSystem.Object);
        setMethodBody.Variables.Add(beforeVariable);
        var afterVariable = new VariableDefinition(typeSystem.Object);
        setMethodBody.Variables.Add(afterVariable);

        index = InsertVariableAssignmentFromCurrentValue(index, property, afterVariable);

        index = instructions.Insert(index,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, property.Name),
            Instruction.Create(OpCodes.Ldloc, beforeVariable),
            Instruction.Create(OpCodes.Ldloc, afterVariable),
            CallEventInvoker(property)
            );

        return AddBeforeVariableAssignment(index, property, beforeVariable);
    }

    int AddSimpleOnChangedCall (int index, MethodReference methodReference)
    {
        return instructions.Insert(index,
            Instruction.Create(OpCodes.Ldarg_0),
            CreateCall(methodReference));
    }

    int AddBeforeAfterOnChangedCall (int index, PropertyDefinition property, MethodReference methodReference)
    {
        var beforeVariable = new VariableDefinition(typeSystem.Object);
        setMethodBody.Variables.Add(beforeVariable);
        var afterVariable = new VariableDefinition(typeSystem.Object);
        setMethodBody.Variables.Add(afterVariable);
        index = InsertVariableAssignmentFromCurrentValue(index, property, afterVariable);

        index = instructions.Insert(index,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, beforeVariable),
            Instruction.Create(OpCodes.Ldloc, afterVariable),
            CreateCall(methodReference)
            );

        return AddBeforeVariableAssignment(index, property, beforeVariable);
    }

    int AddBeforeVariableAssignment (int index, PropertyDefinition property, VariableDefinition beforeVariable)
    {
        var getMethod = property.GetMethod.GetGeneric();

        instructions.Prepend(
            Instruction.Create(OpCodes.Ldarg_0),
            CreateCall(getMethod),
            Instruction.Create(OpCodes.Box, property.GetMethod.ReturnType),
            Instruction.Create(OpCodes.Stloc, beforeVariable));

        return index + 4;
    }

    int InsertVariableAssignmentFromCurrentValue (int index, PropertyDefinition property, VariableDefinition variable)
    {
        var getMethod = property.GetMethod.GetGeneric();

        instructions.Insert(index,
            Instruction.Create(OpCodes.Ldarg_0),
            CreateCall(getMethod),
            Instruction.Create(OpCodes.Box, property.GetMethod.ReturnType),
            Instruction.Create(OpCodes.Stloc, variable));

        return index + 4;
    }

    public Instruction CallEventInvoker (PropertyDefinition propertyDefinition)
    {
        var method = typeNode.EventInvoker.MethodReference;

        if (method.HasGenericParameters)
        {
            var genericMethod = new GenericInstanceMethod(method);
            genericMethod.GenericArguments.Add(propertyDefinition.PropertyType);
            method = genericMethod;
        }

        return Instruction.Create(OpCodes.Callvirt, method);
    }

    public Instruction CreateIsChangedInvoker ()
    {
        return Instruction.Create(OpCodes.Callvirt, typeNode.IsChangedInvoker);
    }

    public Instruction CreateCall (MethodReference methodReference)
    {
        return Instruction.Create(OpCodes.Callvirt, methodReference);
    }
}