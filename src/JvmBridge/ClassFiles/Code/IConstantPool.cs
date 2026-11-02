namespace JvmBridge.ClassFiles.Code;

/// <summary>Where a <see cref="CodeBuilder"/> adds the constants its instructions name: the pool of a class being written or of one being edited.</summary>
internal interface IConstantPool
{
    /// <summary>Gets the class file major version, which decides what the class may contain, for example 52 for Java 8.</summary>
    int MajorVersion { get; }

    /// <summary>Adds an entry to the class's <c>BootstrapMethods</c> attribute, unless one with the same handle and arguments exists, and returns its index there.</summary>
    int AddBootstrapMethod(int handle, IReadOnlyList<int> arguments);

    /// <summary>Lists a nested class in the class's <c>InnerClasses</c> attribute, unless it is listed already.</summary>
    void AddInnerClass(string innerClass, string outerClass, string simpleName, int accessFlags);

    int AddInvokeDynamic(int bootstrapMethod, string name, string descriptor);

    int AddMethodHandle(int referenceKind, ConstantTag referenceTag, string owner, string name, string descriptor);

    int AddMethodType(string descriptor);

    /// <summary>Tells whether a class is the one being written or edited and is an interface.</summary>
    bool IsInterface(string className);

    /// <summary>Tells whether a method is a private method that the class being written or edited declares.</summary>
    bool IsPrivateMethod(string owner, string name, string descriptor);

    int AddClass(string name);

    int AddDouble(double value);

    int AddFieldReference(string owner, string name, string descriptor);

    int AddFloat(float value);

    int AddInteger(int value);

    int AddInterfaceMethodReference(string owner, string name, string descriptor);

    int AddLong(long value);

    int AddMethodReference(string owner, string name, string descriptor);

    int AddString(string value);
}
