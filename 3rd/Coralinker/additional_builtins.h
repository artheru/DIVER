// Auto-generated header
// How to write builtin functions:
//
// 1. Function signature:
//    void builtin_YourFunction(uchar** reptr)
//    The reptr points to the current evaluation stack pointer
//
// 2. Reading arguments:
//    - Arguments are on the stack in reverse order (last argument first)
//    - Use POP macro to move stack pointer: POP;
//    - Use helper functions to get values:
//      * pop_int(reptr)      - for Int32
//      * pop_float(reptr)    - for Single/float
//      * pop_bool(reptr)     - for Boolean
//      * pop_reference(reptr) - for object references
//      * pop_short(reptr)    - for Int16
//      * pop_byte(reptr)     - for Byte
//      * pop_sbyte(reptr)    - for SByte
//
// 3. Returning values:
//    - Use helper functions to push return values:
//      * push_int(reptr, value)      - for Int32
//      * push_float(reptr, value)    - for Single/float
//      * push_bool(reptr, value)     - for Boolean
//      * PUSH_STACK_REFERENCEID(id)  - for object references
//
// 4. Working with objects:
//    - Use heap_obj[id].pointer to access object data
//    - Check object types with header byte:
//      * ArrayHeader (11)   - for arrays
//      * StringHeader (12)  - for strings
//      * ObjectHeader (13)  - for other objects
//
// 5. Error handling:
//    - Use DOOM("message") for fatal errors
//    - Check null references and array bounds
//
// Example:
// void builtin_Math_Add(uchar** reptr) {
//     int b = pop_int(reptr);
//     int a = pop_int(reptr);
//     push_int(reptr, a + b);
// }
//
void builtin_TestFunc(uchar** reptr) {
    // Implementation for TestFunc(int input)
    int a = pop_int(reptr);
    push_int(reptr, a + 10000);
}

void add_additional_builtins() {
    // Start adding methods from index bn
    if (bn >= NUM_BUILTIN_METHODS) {
        DOOM("Too many built-in methods when adding TestFunc!");
    }
    builtin_methods[bn++] = builtin_TestFunc;
}
