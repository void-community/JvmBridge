namespace JvmBridge.Agents;

/// <summary>Why a <see cref="ClassPatch"/> left a class unchanged, so that a tool or a test can tell the cases apart without reading the message.</summary>
public enum PatchSkipCategory
{
    /// <summary>The patch was not skipped.</summary>
    None,

    /// <summary>The patch found nothing to edit, or the class was not valid: the message says which.</summary>
    Other,

    /// <summary>Advice names a method that the class does not declare.</summary>
    MethodNotFound,

    /// <summary>Advice names a constructor or a static initializer, whose code the JVM verifies specially.</summary>
    InitializerNotSupported,

    /// <summary>Advice names a method that has no code: a native or abstract method.</summary>
    NativeOrAbstractMethod,

    /// <summary>Advice names a bridge or synthetic method that the compiler generated.</summary>
    BridgeOrSyntheticMethod,

    /// <summary>Advice names a method of an interface, which advice does not support yet.</summary>
    InterfaceClass,

    /// <summary>A hook's descriptor is not the one that advice on the method needs, or the hook is not static.</summary>
    HookShapeMismatch,

    /// <summary>The method is advised already, by another patch or an earlier application of this one.</summary>
    AlreadyAdvised,

    /// <summary>The method has too many parameters to advise: its locals would not fit in the one-byte slot numbers the stub uses.</summary>
    TooManySlots,

    /// <summary>The class is loaded already and was not advised when it was defined, so the advice would add a method, which the JVM forbids when a class is retransformed.</summary>
    SchemaChangeOnRetransform,
}
