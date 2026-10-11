#include <windows.h>
#include <stdio.h>
#include <jni.h>

typedef jint (JNICALL *create_vm)(JavaVM **, void **, void *);

static int check(JNIEnv *env, const char *operation)
{
    if (!(*env)->ExceptionCheck(env)) return 0;
    fprintf(stderr, "Secondary Java exception in %s\n", operation);
    (*env)->ExceptionDescribe(env);
    (*env)->ExceptionClear(env);
    return 1;
}

static jstring call_string(JNIEnv *env, jobject object, const char *name)
{
    jclass type = (*env)->GetObjectClass(env, object);
    jmethodID method = (*env)->GetMethodID(env, type, name, "()Ljava/lang/String;");
    jvalue empty = {0};
    jstring result = (*env)->CallObjectMethodA(env, object, method, &empty);
    (*env)->DeleteLocalRef(env, type);
    return result;
}

int main(int argc, char **argv)
{
    if (argc < 2 || argc > 3) return 2;
    HMODULE module = LoadLibraryA(argv[1]);
    if (!module) return 3;
    create_vm create = (create_vm)GetProcAddress(module, "JNI_CreateJavaVM");
    if (!create) return 4;
    JavaVMOption option = {"-Xcheck:jni", NULL};
    JavaVMInitArgs configuration = {JNI_VERSION_1_8, 1, &option, JNI_FALSE};
    JavaVM *vm = NULL;
    JNIEnv *env = NULL;
    if (create(&vm, (void **)&env, &configuration) != JNI_OK) return 5;

    if (argc == 3) {
        if ((*env)->PushLocalFrame(env, 8192) != JNI_OK) return 14;
        for (int i = 0; i < 8000; i++) {
            jclass local = (*env)->FindClass(env, "java/lang/Object");
            if (check(env, "stress FindClass") || !local) return 15;
            if ((*env)->GetObjectRefType(env, local) != JNILocalRefType) return 16;
        }
        (*env)->PopLocalFrame(env, NULL);
        printf("C_STRESS_OK\n"); fflush(stdout);
    }

    printf("C_LOOKUP_START\n"); fflush(stdout);
    (*env)->FindClass(env, "jvmbridge/DefinitelyMissingClass");
    if (!(*env)->ExceptionCheck(env)) return 6;
    jthrowable throwable = (*env)->ExceptionOccurred(env);
    (*env)->ExceptionClear(env);
    jclass throwable_type = (*env)->GetObjectClass(env, throwable);
    jstring name = call_string(env, throwable_type, "getName");
    if (check(env, "getName")) return 7;
    jstring message = call_string(env, throwable, "getMessage");
    if (check(env, "getMessage")) return 8;
    jclass writer_type = (*env)->FindClass(env, "java/io/StringWriter");
    jmethodID writer_constructor = (*env)->GetMethodID(env, writer_type, "<init>", "()V");
    jvalue empty = {0};
    jobject writer = (*env)->NewObjectA(env, writer_type, writer_constructor, &empty);
    if (check(env, "StringWriter")) return 9;
    jclass printer_type = (*env)->FindClass(env, "java/io/PrintWriter");
    jmethodID printer_constructor = (*env)->GetMethodID(env, printer_type, "<init>", "(Ljava/io/Writer;Z)V");
    jvalue arguments[2] = {0};
    arguments[0].l = writer;
    arguments[1].z = JNI_TRUE;
    jobject printer = (*env)->NewObjectA(env, printer_type, printer_constructor, arguments);
    if (check(env, "PrintWriter")) return 10;
    jmethodID print = (*env)->GetMethodID(env, throwable_type, "printStackTrace", "(Ljava/io/PrintWriter;)V");
    arguments[0].l = printer;
    (*env)->CallVoidMethodA(env, throwable, print, arguments);
    if (check(env, "printStackTrace")) return 11;
    jstring trace = call_string(env, writer, "toString");
    if (check(env, "toString")) return 12;
    jsize length = (*env)->GetStringLength(env, trace);
    const jchar *characters = (*env)->GetStringChars(env, trace, NULL);
    if (!characters || length == 0) return 13;
    (*env)->ReleaseStringChars(env, trace, characters);
    jobject refs[] = {trace, printer, printer_type, writer, writer_type, message, name, throwable_type, throwable};
    for (int i = 0; i < sizeof(refs) / sizeof(refs[0]); i++) (*env)->DeleteLocalRef(env, refs[i]);
    printf("C_CAPTURE_OK\n"); fflush(stdout);
    return (*vm)->DestroyJavaVM(vm);
}
