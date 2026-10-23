namespace JvmBridge.ClassFiles.Code;

/// <summary>Where a <see cref="CodeBuilder"/> adds the constants its instructions name: the pool of a class being written or of one being edited.</summary>
internal interface IConstantPool
{
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
