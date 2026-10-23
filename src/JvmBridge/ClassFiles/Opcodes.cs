namespace JvmBridge.ClassFiles;

/// <summary>
/// The JVM's opcodes by name, so that code that writes or matches bytecode needs no byte literals. A name is the mnemonic in Pascal case with its type prefix as a capital: <c>aload_0</c> is <see cref="ALoad0"/>, <c>if_icmpeq</c> is
/// <see cref="IfICmpEq"/>, <c>invokevirtual</c> is <see cref="InvokeVirtual"/>. <see cref="Code.CodeBuilder"/> writes instructions with their operands.
/// </summary>
public static class Opcodes
{
    /// <summary><c>nop</c>: does nothing; patches use it to blank out bytes at the same code length.</summary>
    public const byte Nop = 0x00;

    /// <summary><c>aconst_null</c>: pushes the null reference.</summary>
    public const byte AConstNull = 0x01;

    /// <summary><c>iconst_m1</c>: pushes the int -1.</summary>
    public const byte IConstM1 = 0x02;

    /// <summary><c>iconst_0</c>: pushes the int 0.</summary>
    public const byte IConst0 = 0x03;

    /// <summary><c>iconst_1</c>: pushes the int 1.</summary>
    public const byte IConst1 = 0x04;

    /// <summary><c>iconst_2</c>: pushes the int 2.</summary>
    public const byte IConst2 = 0x05;

    /// <summary><c>iconst_3</c>: pushes the int 3.</summary>
    public const byte IConst3 = 0x06;

    /// <summary><c>iconst_4</c>: pushes the int 4.</summary>
    public const byte IConst4 = 0x07;

    /// <summary><c>iconst_5</c>: pushes the int 5.</summary>
    public const byte IConst5 = 0x08;

    /// <summary><c>lconst_0</c>: pushes the long 0.</summary>
    public const byte LConst0 = 0x09;

    /// <summary><c>lconst_1</c>: pushes the long 1.</summary>
    public const byte LConst1 = 0x0a;

    /// <summary><c>fconst_0</c>: pushes the float 0.</summary>
    public const byte FConst0 = 0x0b;

    /// <summary><c>fconst_1</c>: pushes the float 1.</summary>
    public const byte FConst1 = 0x0c;

    /// <summary><c>fconst_2</c>: pushes the float 2.</summary>
    public const byte FConst2 = 0x0d;

    /// <summary><c>dconst_0</c>: pushes the double 0.</summary>
    public const byte DConst0 = 0x0e;

    /// <summary><c>dconst_1</c>: pushes the double 1.</summary>
    public const byte DConst1 = 0x0f;

    /// <summary><c>bipush</c>: pushes its one-byte operand as an int.</summary>
    public const byte BIPush = 0x10;

    /// <summary><c>sipush</c>: pushes its two-byte operand as an int.</summary>
    public const byte SIPush = 0x11;

    /// <summary><c>ldc</c>: pushes an int, float, String or Class constant named by a one-byte constant pool index.</summary>
    public const byte Ldc = 0x12;

    /// <summary><c>ldc_w</c>: pushes an int, float, String or Class constant named by a two-byte constant pool index.</summary>
    public const byte LdcW = 0x13;

    /// <summary><c>ldc2_w</c>: pushes a long or double constant named by a two-byte constant pool index.</summary>
    public const byte Ldc2W = 0x14;

    /// <summary><c>iload</c>: pushes the int in the local variable that its one-byte operand numbers.</summary>
    public const byte ILoad = 0x15;

    /// <summary><c>lload</c>: pushes the long in the local variable that its one-byte operand numbers.</summary>
    public const byte LLoad = 0x16;

    /// <summary><c>fload</c>: pushes the float in the local variable that its one-byte operand numbers.</summary>
    public const byte FLoad = 0x17;

    /// <summary><c>dload</c>: pushes the double in the local variable that its one-byte operand numbers.</summary>
    public const byte DLoad = 0x18;

    /// <summary><c>aload</c>: pushes the reference in the local variable that its one-byte operand numbers.</summary>
    public const byte ALoad = 0x19;

    /// <summary><c>iload_0</c>: pushes the int in local variable 0.</summary>
    public const byte ILoad0 = 0x1a;

    /// <summary><c>iload_1</c>: pushes the int in local variable 1.</summary>
    public const byte ILoad1 = 0x1b;

    /// <summary><c>iload_2</c>: pushes the int in local variable 2.</summary>
    public const byte ILoad2 = 0x1c;

    /// <summary><c>iload_3</c>: pushes the int in local variable 3.</summary>
    public const byte ILoad3 = 0x1d;

    /// <summary><c>lload_0</c>: pushes the long in local variable 0.</summary>
    public const byte LLoad0 = 0x1e;

    /// <summary><c>lload_1</c>: pushes the long in local variable 1.</summary>
    public const byte LLoad1 = 0x1f;

    /// <summary><c>lload_2</c>: pushes the long in local variable 2.</summary>
    public const byte LLoad2 = 0x20;

    /// <summary><c>lload_3</c>: pushes the long in local variable 3.</summary>
    public const byte LLoad3 = 0x21;

    /// <summary><c>fload_0</c>: pushes the float in local variable 0.</summary>
    public const byte FLoad0 = 0x22;

    /// <summary><c>fload_1</c>: pushes the float in local variable 1.</summary>
    public const byte FLoad1 = 0x23;

    /// <summary><c>fload_2</c>: pushes the float in local variable 2.</summary>
    public const byte FLoad2 = 0x24;

    /// <summary><c>fload_3</c>: pushes the float in local variable 3.</summary>
    public const byte FLoad3 = 0x25;

    /// <summary><c>dload_0</c>: pushes the double in local variable 0.</summary>
    public const byte DLoad0 = 0x26;

    /// <summary><c>dload_1</c>: pushes the double in local variable 1.</summary>
    public const byte DLoad1 = 0x27;

    /// <summary><c>dload_2</c>: pushes the double in local variable 2.</summary>
    public const byte DLoad2 = 0x28;

    /// <summary><c>dload_3</c>: pushes the double in local variable 3.</summary>
    public const byte DLoad3 = 0x29;

    /// <summary><c>aload_0</c>: pushes the reference in local variable 0.</summary>
    public const byte ALoad0 = 0x2a;

    /// <summary><c>aload_1</c>: pushes the reference in local variable 1.</summary>
    public const byte ALoad1 = 0x2b;

    /// <summary><c>aload_2</c>: pushes the reference in local variable 2.</summary>
    public const byte ALoad2 = 0x2c;

    /// <summary><c>aload_3</c>: pushes the reference in local variable 3.</summary>
    public const byte ALoad3 = 0x2d;

    /// <summary><c>iaload</c>: pops an array of int and an index, and pushes that element.</summary>
    public const byte IALoad = 0x2e;

    /// <summary><c>laload</c>: pops an array of long and an index, and pushes that element.</summary>
    public const byte LALoad = 0x2f;

    /// <summary><c>faload</c>: pops an array of float and an index, and pushes that element.</summary>
    public const byte FALoad = 0x30;

    /// <summary><c>daload</c>: pops an array of double and an index, and pushes that element.</summary>
    public const byte DALoad = 0x31;

    /// <summary><c>aaload</c>: pops an array of reference and an index, and pushes that element.</summary>
    public const byte AALoad = 0x32;

    /// <summary><c>baload</c>: pops an array of byte or boolean and an index, and pushes that element.</summary>
    public const byte BALoad = 0x33;

    /// <summary><c>caload</c>: pops an array of char and an index, and pushes that element.</summary>
    public const byte CALoad = 0x34;

    /// <summary><c>saload</c>: pops an array of short and an index, and pushes that element.</summary>
    public const byte SALoad = 0x35;

    /// <summary><c>istore</c>: pops a int into the local variable that its one-byte operand numbers.</summary>
    public const byte IStore = 0x36;

    /// <summary><c>lstore</c>: pops a long into the local variable that its one-byte operand numbers.</summary>
    public const byte LStore = 0x37;

    /// <summary><c>fstore</c>: pops a float into the local variable that its one-byte operand numbers.</summary>
    public const byte FStore = 0x38;

    /// <summary><c>dstore</c>: pops a double into the local variable that its one-byte operand numbers.</summary>
    public const byte DStore = 0x39;

    /// <summary><c>astore</c>: pops a reference into the local variable that its one-byte operand numbers.</summary>
    public const byte AStore = 0x3a;

    /// <summary><c>istore_0</c>: pops a int into local variable 0.</summary>
    public const byte IStore0 = 0x3b;

    /// <summary><c>istore_1</c>: pops a int into local variable 1.</summary>
    public const byte IStore1 = 0x3c;

    /// <summary><c>istore_2</c>: pops a int into local variable 2.</summary>
    public const byte IStore2 = 0x3d;

    /// <summary><c>istore_3</c>: pops a int into local variable 3.</summary>
    public const byte IStore3 = 0x3e;

    /// <summary><c>lstore_0</c>: pops a long into local variable 0.</summary>
    public const byte LStore0 = 0x3f;

    /// <summary><c>lstore_1</c>: pops a long into local variable 1.</summary>
    public const byte LStore1 = 0x40;

    /// <summary><c>lstore_2</c>: pops a long into local variable 2.</summary>
    public const byte LStore2 = 0x41;

    /// <summary><c>lstore_3</c>: pops a long into local variable 3.</summary>
    public const byte LStore3 = 0x42;

    /// <summary><c>fstore_0</c>: pops a float into local variable 0.</summary>
    public const byte FStore0 = 0x43;

    /// <summary><c>fstore_1</c>: pops a float into local variable 1.</summary>
    public const byte FStore1 = 0x44;

    /// <summary><c>fstore_2</c>: pops a float into local variable 2.</summary>
    public const byte FStore2 = 0x45;

    /// <summary><c>fstore_3</c>: pops a float into local variable 3.</summary>
    public const byte FStore3 = 0x46;

    /// <summary><c>dstore_0</c>: pops a double into local variable 0.</summary>
    public const byte DStore0 = 0x47;

    /// <summary><c>dstore_1</c>: pops a double into local variable 1.</summary>
    public const byte DStore1 = 0x48;

    /// <summary><c>dstore_2</c>: pops a double into local variable 2.</summary>
    public const byte DStore2 = 0x49;

    /// <summary><c>dstore_3</c>: pops a double into local variable 3.</summary>
    public const byte DStore3 = 0x4a;

    /// <summary><c>astore_0</c>: pops a reference into local variable 0.</summary>
    public const byte AStore0 = 0x4b;

    /// <summary><c>astore_1</c>: pops a reference into local variable 1.</summary>
    public const byte AStore1 = 0x4c;

    /// <summary><c>astore_2</c>: pops a reference into local variable 2.</summary>
    public const byte AStore2 = 0x4d;

    /// <summary><c>astore_3</c>: pops a reference into local variable 3.</summary>
    public const byte AStore3 = 0x4e;

    /// <summary><c>iastore</c>: pops an array of int, an index and a value, and stores the value in that element.</summary>
    public const byte IAStore = 0x4f;

    /// <summary><c>lastore</c>: pops an array of long, an index and a value, and stores the value in that element.</summary>
    public const byte LAStore = 0x50;

    /// <summary><c>fastore</c>: pops an array of float, an index and a value, and stores the value in that element.</summary>
    public const byte FAStore = 0x51;

    /// <summary><c>dastore</c>: pops an array of double, an index and a value, and stores the value in that element.</summary>
    public const byte DAStore = 0x52;

    /// <summary><c>aastore</c>: pops an array of reference, an index and a value, and stores the value in that element.</summary>
    public const byte AAStore = 0x53;

    /// <summary><c>bastore</c>: pops an array of byte or boolean, an index and a value, and stores the value in that element.</summary>
    public const byte BAStore = 0x54;

    /// <summary><c>castore</c>: pops an array of char, an index and a value, and stores the value in that element.</summary>
    public const byte CAStore = 0x55;

    /// <summary><c>sastore</c>: pops an array of short, an index and a value, and stores the value in that element.</summary>
    public const byte SAStore = 0x56;

    /// <summary><c>pop</c>: discards the value on top of the stack.</summary>
    public const byte Pop = 0x57;

    /// <summary><c>pop2</c>: discards the top two stack slots: one long or double, or two other values.</summary>
    public const byte Pop2 = 0x58;

    /// <summary><c>dup</c>: pushes a copy of the value on top of the stack.</summary>
    public const byte Dup = 0x59;

    /// <summary><c>dup_x1</c>: copies the top value under the second one.</summary>
    public const byte DupX1 = 0x5a;

    /// <summary><c>dup_x2</c>: copies the top value under the next two slots.</summary>
    public const byte DupX2 = 0x5b;

    /// <summary><c>dup2</c>: pushes a copy of the top two slots.</summary>
    public const byte Dup2 = 0x5c;

    /// <summary><c>dup2_x1</c>: copies the top two slots under the third.</summary>
    public const byte Dup2X1 = 0x5d;

    /// <summary><c>dup2_x2</c>: copies the top two slots under the next two.</summary>
    public const byte Dup2X2 = 0x5e;

    /// <summary><c>swap</c>: exchanges the top two values.</summary>
    public const byte Swap = 0x5f;

    /// <summary><c>iadd</c>: pops two int values and pushes their sum.</summary>
    public const byte IAdd = 0x60;

    /// <summary><c>ladd</c>: pops two long values and pushes their sum.</summary>
    public const byte LAdd = 0x61;

    /// <summary><c>fadd</c>: pops two float values and pushes their sum.</summary>
    public const byte FAdd = 0x62;

    /// <summary><c>dadd</c>: pops two double values and pushes their sum.</summary>
    public const byte DAdd = 0x63;

    /// <summary><c>isub</c>: subtracts the int on top of the stack from the one under it.</summary>
    public const byte ISub = 0x64;

    /// <summary><c>lsub</c>: subtracts the long on top of the stack from the one under it.</summary>
    public const byte LSub = 0x65;

    /// <summary><c>fsub</c>: subtracts the float on top of the stack from the one under it.</summary>
    public const byte FSub = 0x66;

    /// <summary><c>dsub</c>: subtracts the double on top of the stack from the one under it.</summary>
    public const byte DSub = 0x67;

    /// <summary><c>imul</c>: pops two int values and pushes their product.</summary>
    public const byte IMul = 0x68;

    /// <summary><c>lmul</c>: pops two long values and pushes their product.</summary>
    public const byte LMul = 0x69;

    /// <summary><c>fmul</c>: pops two float values and pushes their product.</summary>
    public const byte FMul = 0x6a;

    /// <summary><c>dmul</c>: pops two double values and pushes their product.</summary>
    public const byte DMul = 0x6b;

    /// <summary><c>idiv</c>: divides the int under the top of the stack by the one on top.</summary>
    public const byte IDiv = 0x6c;

    /// <summary><c>ldiv</c>: divides the long under the top of the stack by the one on top.</summary>
    public const byte LDiv = 0x6d;

    /// <summary><c>fdiv</c>: divides the float under the top of the stack by the one on top.</summary>
    public const byte FDiv = 0x6e;

    /// <summary><c>ddiv</c>: divides the double under the top of the stack by the one on top.</summary>
    public const byte DDiv = 0x6f;

    /// <summary><c>irem</c>: pushes the remainder of dividing the int under the top of the stack by the one on top.</summary>
    public const byte IRem = 0x70;

    /// <summary><c>lrem</c>: pushes the remainder of dividing the long under the top of the stack by the one on top.</summary>
    public const byte LRem = 0x71;

    /// <summary><c>frem</c>: pushes the remainder of dividing the float under the top of the stack by the one on top.</summary>
    public const byte FRem = 0x72;

    /// <summary><c>drem</c>: pushes the remainder of dividing the double under the top of the stack by the one on top.</summary>
    public const byte DRem = 0x73;

    /// <summary><c>ineg</c>: negates the int on top of the stack.</summary>
    public const byte INeg = 0x74;

    /// <summary><c>lneg</c>: negates the long on top of the stack.</summary>
    public const byte LNeg = 0x75;

    /// <summary><c>fneg</c>: negates the float on top of the stack.</summary>
    public const byte FNeg = 0x76;

    /// <summary><c>dneg</c>: negates the double on top of the stack.</summary>
    public const byte DNeg = 0x77;

    /// <summary><c>ishl</c>: shifts the int under the top left by the int on top of the stack.</summary>
    public const byte IShl = 0x78;

    /// <summary><c>lshl</c>: shifts the long under the top left by the int on top of the stack.</summary>
    public const byte LShl = 0x79;

    /// <summary><c>ishr</c>: shifts the int under the top right by the int on top of the stack, keeping its sign.</summary>
    public const byte IShr = 0x7a;

    /// <summary><c>lshr</c>: shifts the long under the top right by the int on top of the stack, keeping its sign.</summary>
    public const byte LShr = 0x7b;

    /// <summary><c>iushr</c>: shifts the int under the top right by the int on top of the stack, filling with zeros.</summary>
    public const byte IUShr = 0x7c;

    /// <summary><c>lushr</c>: shifts the long under the top right by the int on top of the stack, filling with zeros.</summary>
    public const byte LUShr = 0x7d;

    /// <summary><c>iand</c>: pops two int values and pushes their bitwise and.</summary>
    public const byte IAnd = 0x7e;

    /// <summary><c>land</c>: pops two long values and pushes their bitwise and.</summary>
    public const byte LAnd = 0x7f;

    /// <summary><c>ior</c>: pops two int values and pushes their bitwise or.</summary>
    public const byte IOr = 0x80;

    /// <summary><c>lor</c>: pops two long values and pushes their bitwise or.</summary>
    public const byte LOr = 0x81;

    /// <summary><c>ixor</c>: pops two int values and pushes their bitwise exclusive or.</summary>
    public const byte IXor = 0x82;

    /// <summary><c>lxor</c>: pops two long values and pushes their bitwise exclusive or.</summary>
    public const byte LXor = 0x83;

    /// <summary><c>iinc</c>: adds a signed one-byte constant to an int local variable.</summary>
    public const byte IInc = 0x84;

    /// <summary><c>i2l</c>: converts the int on top of the stack to a long.</summary>
    public const byte I2L = 0x85;

    /// <summary><c>i2f</c>: converts the int on top of the stack to a float.</summary>
    public const byte I2F = 0x86;

    /// <summary><c>i2d</c>: converts the int on top of the stack to a double.</summary>
    public const byte I2D = 0x87;

    /// <summary><c>l2i</c>: converts the long on top of the stack to a int.</summary>
    public const byte L2I = 0x88;

    /// <summary><c>l2f</c>: converts the long on top of the stack to a float.</summary>
    public const byte L2F = 0x89;

    /// <summary><c>l2d</c>: converts the long on top of the stack to a double.</summary>
    public const byte L2D = 0x8a;

    /// <summary><c>f2i</c>: converts the float on top of the stack to a int.</summary>
    public const byte F2I = 0x8b;

    /// <summary><c>f2l</c>: converts the float on top of the stack to a long.</summary>
    public const byte F2L = 0x8c;

    /// <summary><c>f2d</c>: converts the float on top of the stack to a double.</summary>
    public const byte F2D = 0x8d;

    /// <summary><c>d2i</c>: converts the double on top of the stack to a int.</summary>
    public const byte D2I = 0x8e;

    /// <summary><c>d2l</c>: converts the double on top of the stack to a long.</summary>
    public const byte D2L = 0x8f;

    /// <summary><c>d2f</c>: converts the double on top of the stack to a float.</summary>
    public const byte D2F = 0x90;

    /// <summary><c>i2b</c>: converts the int on top of the stack to a byte.</summary>
    public const byte I2B = 0x91;

    /// <summary><c>i2c</c>: converts the int on top of the stack to a char.</summary>
    public const byte I2C = 0x92;

    /// <summary><c>i2s</c>: converts the int on top of the stack to a short.</summary>
    public const byte I2S = 0x93;

    /// <summary><c>lcmp</c>: pops two longs and pushes -1, 0 or 1 as the first is less than, equal to or greater than the second.</summary>
    public const byte LCmp = 0x94;

    /// <summary><c>fcmpl</c>: compares two floats into -1, 0 or 1, giving -1 when either is NaN.</summary>
    public const byte FCmpL = 0x95;

    /// <summary><c>fcmpg</c>: compares two floats into -1, 0 or 1, giving 1 when either is NaN.</summary>
    public const byte FCmpG = 0x96;

    /// <summary><c>dcmpl</c>: compares two doubles into -1, 0 or 1, giving -1 when either is NaN.</summary>
    public const byte DCmpL = 0x97;

    /// <summary><c>dcmpg</c>: compares two doubles into -1, 0 or 1, giving 1 when either is NaN.</summary>
    public const byte DCmpG = 0x98;

    /// <summary><c>ifeq</c>: pops an int and jumps by its two-byte offset when the int is equal to zero.</summary>
    public const byte IfEq = 0x99;

    /// <summary><c>ifne</c>: pops an int and jumps by its two-byte offset when the int is not equal to zero.</summary>
    public const byte IfNe = 0x9a;

    /// <summary><c>iflt</c>: pops an int and jumps by its two-byte offset when the int is less than zero.</summary>
    public const byte IfLt = 0x9b;

    /// <summary><c>ifge</c>: pops an int and jumps by its two-byte offset when the int is greater than or equal to zero.</summary>
    public const byte IfGe = 0x9c;

    /// <summary><c>ifgt</c>: pops an int and jumps by its two-byte offset when the int is greater than zero.</summary>
    public const byte IfGt = 0x9d;

    /// <summary><c>ifle</c>: pops an int and jumps by its two-byte offset when the int is less than or equal to zero.</summary>
    public const byte IfLe = 0x9e;

    /// <summary><c>if_icmpeq</c>: pops two ints and jumps by its two-byte offset when the first is equal to the second.</summary>
    public const byte IfICmpEq = 0x9f;

    /// <summary><c>if_icmpne</c>: pops two ints and jumps by its two-byte offset when the first is not equal to the second.</summary>
    public const byte IfICmpNe = 0xa0;

    /// <summary><c>if_icmplt</c>: pops two ints and jumps by its two-byte offset when the first is less than the second.</summary>
    public const byte IfICmpLt = 0xa1;

    /// <summary><c>if_icmpge</c>: pops two ints and jumps by its two-byte offset when the first is greater than or equal to the second.</summary>
    public const byte IfICmpGe = 0xa2;

    /// <summary><c>if_icmpgt</c>: pops two ints and jumps by its two-byte offset when the first is greater than the second.</summary>
    public const byte IfICmpGt = 0xa3;

    /// <summary><c>if_icmple</c>: pops two ints and jumps by its two-byte offset when the first is less than or equal to the second.</summary>
    public const byte IfICmpLe = 0xa4;

    /// <summary><c>if_acmpeq</c>: pops two references and jumps by its two-byte offset when they are the same object.</summary>
    public const byte IfACmpEq = 0xa5;

    /// <summary><c>if_acmpne</c>: pops two references and jumps by its two-byte offset when they are different objects.</summary>
    public const byte IfACmpNe = 0xa6;

    /// <summary><c>goto</c>: jumps by its two-byte offset.</summary>
    public const byte Goto = 0xa7;

    /// <summary><c>jsr</c>: jumps to a subroutine, pushing the return address; class files of version 51 and later cannot use it.</summary>
    public const byte Jsr = 0xa8;

    /// <summary><c>ret</c>: returns from a subroutine to the address in a local variable; not allowed from version 51.</summary>
    public const byte Ret = 0xa9;

    /// <summary><c>tableswitch</c>: jumps through a table of offsets indexed by the int on top of the stack.</summary>
    public const byte TableSwitch = 0xaa;

    /// <summary><c>lookupswitch</c>: jumps to the offset paired with the int on top of the stack, or to the default.</summary>
    public const byte LookupSwitch = 0xab;

    /// <summary><c>ireturn</c>: returns the int on top of the stack to the caller.</summary>
    public const byte IReturn = 0xac;

    /// <summary><c>lreturn</c>: returns the long on top of the stack to the caller.</summary>
    public const byte LReturn = 0xad;

    /// <summary><c>freturn</c>: returns the float on top of the stack to the caller.</summary>
    public const byte FReturn = 0xae;

    /// <summary><c>dreturn</c>: returns the double on top of the stack to the caller.</summary>
    public const byte DReturn = 0xaf;

    /// <summary><c>areturn</c>: returns the reference on top of the stack to the caller.</summary>
    public const byte AReturn = 0xb0;

    /// <summary><c>return</c>: returns from a void method.</summary>
    public const byte Return = 0xb1;

    /// <summary><c>getstatic</c>: pushes the value of a static field.</summary>
    public const byte GetStatic = 0xb2;

    /// <summary><c>putstatic</c>: pops a value into a static field.</summary>
    public const byte PutStatic = 0xb3;

    /// <summary><c>getfield</c>: pops an object and pushes the value of one of its fields.</summary>
    public const byte GetField = 0xb4;

    /// <summary><c>putfield</c>: pops an object and a value and stores the value in one of its fields.</summary>
    public const byte PutField = 0xb5;

    /// <summary><c>invokevirtual</c>: calls an instance method of a class, chosen by the receiver's class at run time.</summary>
    public const byte InvokeVirtual = 0xb6;

    /// <summary><c>invokespecial</c>: calls a constructor, a private method or a superclass method without virtual dispatch.</summary>
    public const byte InvokeSpecial = 0xb7;

    /// <summary><c>invokestatic</c>: calls a static method.</summary>
    public const byte InvokeStatic = 0xb8;

    /// <summary><c>invokeinterface</c>: calls a method declared by an interface.</summary>
    public const byte InvokeInterface = 0xb9;

    /// <summary><c>invokedynamic</c>: calls the target that a bootstrap method links the call site to, as lambdas and string concatenation do.</summary>
    public const byte InvokeDynamic = 0xba;

    /// <summary><c>new</c>: pushes a new, not yet initialized object of a class.</summary>
    public const byte New = 0xbb;

    /// <summary><c>newarray</c>: pops a length and pushes a new array of a primitive type.</summary>
    public const byte NewArray = 0xbc;

    /// <summary><c>anewarray</c>: pops a length and pushes a new array of references of a class.</summary>
    public const byte ANewArray = 0xbd;

    /// <summary><c>arraylength</c>: pops an array and pushes its length.</summary>
    public const byte ArrayLength = 0xbe;

    /// <summary><c>athrow</c>: throws the exception object on top of the stack.</summary>
    public const byte AThrow = 0xbf;

    /// <summary><c>checkcast</c>: fails with ClassCastException unless the reference on top of the stack is null or of a class.</summary>
    public const byte CheckCast = 0xc0;

    /// <summary><c>instanceof</c>: pops a reference and pushes 1 when it is an instance of a class, 0 otherwise.</summary>
    public const byte InstanceOf = 0xc1;

    /// <summary><c>monitorenter</c>: pops an object and takes its monitor, as entering a synchronized block does.</summary>
    public const byte MonitorEnter = 0xc2;

    /// <summary><c>monitorexit</c>: pops an object and releases its monitor.</summary>
    public const byte MonitorExit = 0xc3;

    /// <summary><c>wide</c>: gives the following local variable instruction a two-byte index.</summary>
    public const byte Wide = 0xc4;

    /// <summary><c>multianewarray</c>: pops the lengths of several dimensions and pushes a new multidimensional array.</summary>
    public const byte MultiANewArray = 0xc5;

    /// <summary><c>ifnull</c>: pops a reference and jumps by its two-byte offset when it is null.</summary>
    public const byte IfNull = 0xc6;

    /// <summary><c>ifnonnull</c>: pops a reference and jumps by its two-byte offset unless it is null.</summary>
    public const byte IfNonNull = 0xc7;

    /// <summary><c>goto_w</c>: jumps by its four-byte offset.</summary>
    public const byte GotoW = 0xc8;

    /// <summary><c>jsr_w</c>: jumps to a subroutine by a four-byte offset; class files of version 51 and later cannot use it.</summary>
    public const byte JsrW = 0xc9;

    // Fixed instruction lengths by opcode; 0 marks switches, wide and reserved opcodes.
    private static readonly byte[] _lengths = BuildLengths();

    // Pops in the high nibble and pushes in the low one, for the instructions of one byte.
    private static readonly byte?[] _effects = BuildEffects();

    /// <summary>Gets the length of the instruction at an offset, validating that it fits in the code.</summary>
    internal static int GetLength(ReadOnlySpan<byte> code, int offset)
    {
        byte opcode = code[offset];
        int length;
        if (opcode == TableSwitch)
        {
            int table = offset + 1 + Padding(offset);
            RequireAvailable(code, table, 12, offset);
            int low = ReadInt(code, table + 4);
            int high = ReadInt(code, table + 8);
            long entries = (long)high - low + 1;
            if (high < low || entries > code.Length)
                throw new ClassFormatException($"Invalid tableswitch bounds at code offset {offset}: low {low}, high {high}.");
            length = 1 + Padding(offset) + 12 + (int)(entries * 4);
        }
        else if (opcode == LookupSwitch)
        {
            int table = offset + 1 + Padding(offset);
            RequireAvailable(code, table, 8, offset);
            int pairs = ReadInt(code, table + 4);
            if (pairs < 0 || pairs > code.Length)
                throw new ClassFormatException($"Invalid lookupswitch pair count at code offset {offset}.");
            length = 1 + Padding(offset) + 8 + (pairs * 8);
        }
        else if (opcode == Wide)
        {
            RequireAvailable(code, offset, 2, offset);
            byte modified = code[offset + 1];
            if (modified == IInc)
                length = 6;
            else if (modified is (>= 0x15 and <= 0x19) or (>= 0x36 and <= 0x3a) or Ret)
                length = 4;
            else
                throw new ClassFormatException($"Opcode 0x{modified:x2} cannot follow wide at code offset {offset}.");
        }
        else
        {
            length = _lengths[opcode];
            if (length == 0)
                throw new ClassFormatException($"Unknown or reserved opcode 0x{opcode:x2} at code offset {offset}.");
        }
        if (offset + length > code.Length)
            throw new ClassFormatException($"Instruction at code offset {offset} runs past the end of the code.");
        return length;
    }

    /// <summary>Gets the constant pool index operand of an instruction, or <see langword="null"/> when it has none.</summary>
    internal static int? GetConstantIndex(ReadOnlySpan<byte> code, int offset)
    {
        byte opcode = code[offset];
        if (opcode == Ldc)
            return code[offset + 1];
        bool takesIndex = opcode is LdcW or Ldc2W or (>= GetStatic and <= New) or ANewArray or CheckCast or InstanceOf or MultiANewArray;
        return takesIndex ? (code[offset + 1] << 8) | code[offset + 2] : null;
    }

    /// <summary>Gets whether an instruction can reference a constant of this kind.</summary>
    internal static bool AcceptsConstant(byte opcode, ConstantTag tag)
    {
        return opcode switch
        {
            Ldc or LdcW => tag is ConstantTag.Integer or ConstantTag.Float or ConstantTag.String or ConstantTag.Class or ConstantTag.MethodType or ConstantTag.MethodHandle or ConstantTag.Dynamic,
            Ldc2W => tag is ConstantTag.Long or ConstantTag.Double or ConstantTag.Dynamic,
            >= GetStatic and <= PutField => tag is ConstantTag.Fieldref,
            InvokeVirtual => tag is ConstantTag.Methodref,
            InvokeSpecial or InvokeStatic => tag is ConstantTag.Methodref or ConstantTag.InterfaceMethodref,
            InvokeInterface => tag is ConstantTag.InterfaceMethodref,
            InvokeDynamic => tag is ConstantTag.InvokeDynamic,
            New or ANewArray or CheckCast or InstanceOf or MultiANewArray => tag is ConstantTag.Class,
            _ => false,
        };
    }

    /// <summary>Counts the operand slots of a method descriptor's parameters: two for long and double, one for everything else.</summary>
    internal static int CountArgumentSlots(string descriptor)
    {
        if (descriptor.Length == 0 || descriptor[0] != '(')
            throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");

        int slots = 0;

        for (int index = 1; index < descriptor.Length && descriptor[index] != ')'; index++)
        {
            char type = descriptor[index];

            if (type is 'J' or 'D')
                slots += 2;
            else
            {
                slots++;

                while (index < descriptor.Length && descriptor[index] == '[')
                    index++;

                if (index < descriptor.Length && descriptor[index] == 'L')
                    index = descriptor.IndexOf(';', index);

                if (index < 0 || index >= descriptor.Length)
                    throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");
            }
        }

        return slots;
    }

    /// <summary>Gets how many stack slots a one-byte instruction pops and pushes, or <see langword="null"/> for an instruction with operands or one whose effect depends on them.</summary>
    internal static (int Pops, int Pushes)? GetStackEffect(byte opcode) => _effects[opcode] is { } effect ? (effect >> 4, effect & 0xf) : null;

    private static int Padding(int offset) => (4 - ((offset + 1) % 4)) % 4;

    private static void RequireAvailable(ReadOnlySpan<byte> code, int start, int count, int instruction)
    {
        if (start + count > code.Length)
            throw new ClassFormatException($"Instruction at code offset {instruction} runs past the end of the code.");
    }

    private static int ReadInt(ReadOnlySpan<byte> code, int offset) => (code[offset] << 24) | (code[offset + 1] << 16) | (code[offset + 2] << 8) | code[offset + 3];

    private static byte?[] BuildEffects()
    {
        byte?[] effects = new byte?[256];

        void Set(int first, int last, int pops, int pushes)
        {
            for (int opcode = first; opcode <= last; opcode++)
                effects[opcode] = (byte)((pops << 4) | pushes);
        }

        Set(Nop, Nop, pops: 0, pushes: 0);
        Set(AConstNull, IConst5, pops: 0, pushes: 1);
        Set(LConst0, LConst1, pops: 0, pushes: 2);
        Set(FConst0, FConst2, pops: 0, pushes: 1);
        Set(DConst0, DConst1, pops: 0, pushes: 2);
        Set(ILoad0, ILoad3, pops: 0, pushes: 1);
        Set(LLoad0, LLoad3, pops: 0, pushes: 2);
        Set(FLoad0, FLoad3, pops: 0, pushes: 1);
        Set(DLoad0, DLoad3, pops: 0, pushes: 2);
        Set(ALoad0, ALoad3, pops: 0, pushes: 1);
        Set(IALoad, IALoad, pops: 2, pushes: 1);
        Set(LALoad, LALoad, pops: 2, pushes: 2);
        Set(FALoad, FALoad, pops: 2, pushes: 1);
        Set(DALoad, DALoad, pops: 2, pushes: 2);
        Set(AALoad, SALoad, pops: 2, pushes: 1);
        Set(IStore0, IStore3, pops: 1, pushes: 0);
        Set(LStore0, LStore3, pops: 2, pushes: 0);
        Set(FStore0, FStore3, pops: 1, pushes: 0);
        Set(DStore0, DStore3, pops: 2, pushes: 0);
        Set(AStore0, AStore3, pops: 1, pushes: 0);
        Set(IAStore, IAStore, pops: 3, pushes: 0);
        Set(LAStore, LAStore, pops: 4, pushes: 0);
        Set(FAStore, FAStore, pops: 3, pushes: 0);
        Set(DAStore, DAStore, pops: 4, pushes: 0);
        Set(AAStore, SAStore, pops: 3, pushes: 0);
        Set(Pop, Pop, pops: 1, pushes: 0);
        Set(Pop2, Pop2, pops: 2, pushes: 0);
        Set(Dup, Dup, pops: 1, pushes: 2);
        Set(DupX1, DupX1, pops: 2, pushes: 3);
        Set(DupX2, DupX2, pops: 3, pushes: 4);
        Set(Dup2, Dup2, pops: 2, pushes: 4);
        Set(Dup2X1, Dup2X1, pops: 3, pushes: 5);
        Set(Dup2X2, Dup2X2, pops: 4, pushes: 6);
        Set(Swap, Swap, pops: 2, pushes: 2);

        // Arithmetic comes in int, long, float and double groups of four: two slots per operand for long and double.
        for (int group = IAdd; group <= DRem; group += 4)
        {
            Set(group, group, pops: 2, pushes: 1);
            Set(group + 1, group + 1, pops: 4, pushes: 2);
            Set(group + 2, group + 2, pops: 2, pushes: 1);
            Set(group + 3, group + 3, pops: 4, pushes: 2);
        }

        Set(INeg, INeg, pops: 1, pushes: 1);
        Set(LNeg, LNeg, pops: 2, pushes: 2);
        Set(FNeg, FNeg, pops: 1, pushes: 1);
        Set(DNeg, DNeg, pops: 2, pushes: 2);

        foreach ((byte intShift, byte longShift) in new[] { (IShl, LShl), (IShr, LShr), (IUShr, LUShr) })
        {
            Set(intShift, intShift, pops: 2, pushes: 1);
            Set(longShift, longShift, pops: 3, pushes: 2);
        }

        foreach ((byte intLogic, byte longLogic) in new[] { (IAnd, LAnd), (IOr, LOr), (IXor, LXor) })
        {
            Set(intLogic, intLogic, pops: 2, pushes: 1);
            Set(longLogic, longLogic, pops: 4, pushes: 2);
        }

        Set(I2L, I2L, pops: 1, pushes: 2);
        Set(I2F, I2F, pops: 1, pushes: 1);
        Set(I2D, I2D, pops: 1, pushes: 2);
        Set(L2I, L2F, pops: 2, pushes: 1);
        Set(L2D, L2D, pops: 2, pushes: 2);
        Set(F2I, F2I, pops: 1, pushes: 1);
        Set(F2L, F2D, pops: 1, pushes: 2);
        Set(D2I, D2I, pops: 2, pushes: 1);
        Set(D2L, D2L, pops: 2, pushes: 2);
        Set(D2F, D2F, pops: 2, pushes: 1);
        Set(I2B, I2S, pops: 1, pushes: 1);
        Set(LCmp, LCmp, pops: 4, pushes: 1);
        Set(FCmpL, FCmpG, pops: 2, pushes: 1);
        Set(DCmpL, DCmpG, pops: 4, pushes: 1);
        Set(IReturn, IReturn, pops: 1, pushes: 0);
        Set(LReturn, LReturn, pops: 2, pushes: 0);
        Set(FReturn, FReturn, pops: 1, pushes: 0);
        Set(DReturn, DReturn, pops: 2, pushes: 0);
        Set(AReturn, AReturn, pops: 1, pushes: 0);
        Set(Return, Return, pops: 0, pushes: 0);
        Set(ArrayLength, ArrayLength, pops: 1, pushes: 1);
        Set(AThrow, AThrow, pops: 1, pushes: 0);
        Set(MonitorEnter, MonitorExit, pops: 1, pushes: 0);

        return effects;
    }

    private static byte[] BuildLengths()
    {
        byte[] lengths = new byte[256];
        void Fill(int first, int last, byte length)
        {
            for (int opcode = first; opcode <= last; opcode++)
                lengths[opcode] = length;
        }
        Fill(0x00, 0x0f, 1);
        lengths[0x10] = 2;
        lengths[0x11] = 3;
        lengths[0x12] = 2;
        lengths[0x13] = 3;
        lengths[0x14] = 3;
        Fill(0x15, 0x19, 2);
        Fill(0x1a, 0x35, 1);
        Fill(0x36, 0x3a, 2);
        Fill(0x3b, 0x83, 1);
        lengths[0x84] = 3;
        Fill(0x85, 0x98, 1);
        Fill(0x99, 0xa8, 3);
        lengths[0xa9] = 2;
        Fill(0xac, 0xb1, 1);
        Fill(0xb2, 0xb8, 3);
        lengths[0xb9] = 5;
        lengths[0xba] = 5;
        lengths[0xbb] = 3;
        lengths[0xbc] = 2;
        lengths[0xbd] = 3;
        lengths[0xbe] = 1;
        lengths[0xbf] = 1;
        lengths[0xc0] = 3;
        lengths[0xc1] = 3;
        lengths[0xc2] = 1;
        lengths[0xc3] = 1;
        lengths[0xc5] = 4;
        lengths[0xc6] = 3;
        lengths[0xc7] = 3;
        lengths[0xc8] = 5;
        lengths[0xc9] = 5;
        return lengths;
    }
}
