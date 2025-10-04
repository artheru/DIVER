// DIVER C Code Generation
// This file contains C code generated from .NET methods for MCU compilation
// To compile this code, ensure you have an ARM embedded toolchain installed
// Visit: https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads

#include <math.h>  // Math functions support

#define i1 char
#define u1 unsigned char
#define i2 short
#define u2 unsigned short
#define i4 int
#define u4 unsigned int
#define r4 float
#define ptr void*

// function begins

void cfun0(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: ; //IL_0001: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_0006:
IL_0007: return; //IL_0007: ret: s_0, pop0, push0
}

void cfun1(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: ; //IL_0001: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_0006:
IL_0007: return; //IL_0007: ret: s_0, pop0, push0
}

void cfun2(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: stack_1_i4=(2); //IL_0001: ldc.i4.2: s_1, pop0, push1
IL_0002: *(i4*)&(stack_0_ptr[589824+1])=stack_1_i4; //IL_0002: stfld System.Int32 DiverTest.DerivedProcessor::multiplier: s_2, pop2, push0
IL_0007: stack_0_ptr=(arg0); //IL_0007: ldarg.0: s_0, pop0, push1
IL_000d:
IL_000e: return; //IL_000e: ret: s_0, pop0, push0
}

void cfun3(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: stack_1_i4=(100); //IL_0001: ldc.i4.s 100: s_1, pop0, push1
IL_0003: *(i4*)&(stack_0_ptr[655360+1])=stack_1_i4; //IL_0003: stfld System.Int32 DiverTest.BaseProcessor::baseValue: s_2, pop2, push0
IL_0008: stack_0_ptr=(arg0); //IL_0008: ldarg.0: s_0, pop0, push1
IL_0009: ; //IL_0009: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_000e:
IL_000f: return; //IL_000f: ret: s_0, pop0, push0
}

void cfun4(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
r4 stack_1_r4;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: stack_1_r4=(3.1f); //IL_0001: ldc.r4 3.14: s_1, pop0, push1
IL_0006: *(int*)&(stack_0_ptr[720896+1])=*(int*)&stack_1_r4; //IL_0006: stfld System.Single DiverTest.TestLogic/DataProcessor::coefficient: s_2, pop2, push0
IL_000b: stack_0_ptr=(arg0); //IL_000b: ldarg.0: s_0, pop0, push1
IL_000c: ; //IL_000c: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_0011:
IL_0012: return; //IL_0012: ret: s_0, pop0, push0
}

void cfun5(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
i4 arg1 = *(i4*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: ; //IL_0001: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_0006:
IL_0007: stack_0_ptr=(arg0); //IL_0007: ldarg.0: s_0, pop0, push1
IL_0008: stack_1_i4=(arg1); //IL_0008: ldarg.1: s_1, pop0, push1
IL_0009: *(i4*)&(stack_0_ptr[786432+1])=stack_1_i4; //IL_0009: stfld System.Int32 DiverTest.TestLogic/DataProcessor/<ProcessStream>d__4::<>1__state: s_2, pop2, push0
IL_000e: return; //IL_000e: ret: s_0, pop0, push0
}

i4 cfun6(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
i4 arg1 = *(i4*)&args[(argN++)*4];
i4 arg2 = *(i4*)&args[(argN++)*4];
//stack_vars:
i4 stack_0_i4;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_i4=(arg1); //IL_0000: ldarg.1: s_0, pop0, push1
IL_0001: stack_1_i4=(arg2); //IL_0001: ldarg.2: s_1, pop0, push1
IL_0002: stack_0_i4=((stack_0_i4)+(stack_1_i4)); //IL_0002: add: s_2, pop2, push1
IL_0003: return stack_0_i4; //IL_0003: ret: s_1, pop1, push0
}

u1 cfun7(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
i4 arg1 = *(i4*)&args[(argN++)*4];
//stack_vars:
i4 stack_0_i4;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_i4=(arg1); //IL_0000: ldarg.1: s_0, pop0, push1
IL_0001: stack_1_i4=(2); //IL_0001: ldc.i4.2: s_1, pop0, push1
IL_0002: stack_0_i4=((stack_0_i4)%(stack_1_i4)); //IL_0002: rem: s_2, pop2, push1
IL_0003: stack_1_i4=(0); //IL_0003: ldc.i4.0: s_1, pop0, push1
IL_0004: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_0004: ceq: s_2, pop2, push1
IL_0006: return stack_0_i4; //IL_0006: ret: s_1, pop1, push0
}

i4 cfun8(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
i4 arg1 = *(i4*)&args[(argN++)*4];
//stack_vars:
i4 stack_0_i4;;
i4 stack_1_i4;;
 //local_vars:
;
 IL_0000: stack_0_i4=(arg1); //IL_0000: ldarg.1: s_0, pop0, push1
IL_0001: stack_1_i4=(2); //IL_0001: ldc.i4.2: s_1, pop0, push1
IL_0002: stack_0_i4=((stack_0_i4)*(stack_1_i4)); //IL_0002: mul: s_2, pop2, push1
IL_0003: return stack_0_i4; //IL_0003: ret: s_1, pop1, push0
}

void cfun9(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
 //local_vars:
;
 IL_0000: stack_0_ptr=(arg0); //IL_0000: ldarg.0: s_0, pop0, push1
IL_0001: ; //IL_0001: call System.Void System.Object::.ctor(): s_1, pop1, push0
IL_0006:
IL_0007: return; //IL_0007: ret: s_0, pop0, push0
}

r4 cfun10(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
r4 stack_0_r4;;
 //local_vars:
;
 IL_0000: stack_0_r4=(1.5f); //IL_0000: ldc.r4 1.5: s_0, pop0, push1
IL_0005: return stack_0_r4; //IL_0005: ret: s_1, pop1, push0
}

