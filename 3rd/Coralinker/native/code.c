#define i1 char 
#define u1 unsigned char
#define i2 short
#define u2 unsigned short
#define i4 int
#define u4 unsigned int
#define r4 float
#define ptr void*

// function begins

i4 cfun0(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_0_i4;;
u1 stack_0_u1;;
i4 stack_1_i4;;
ptr stack_1_ptr;;
i4 stack_2_i4;;
u1 stack_1_u1;;
 //local_vars:
i4 var0;
u1 var1;
i4 var2;
 IL_0000:
IL_0001: stack_0_ptr=(arg0); //IL_0001: ldarg.0: s_0, pop0, push1
IL_0002: if (!(stack_0_ptr)) goto IL_000f; //IL_0002: brfalse.s IL_000f: s_1, pop1, push0
IL_0004: stack_0_ptr=(arg0); //IL_0004: ldarg.0: s_0, pop0, push1
IL_0005: stack_0_i4=(*(((i4*)stack_0_ptr)-1)); //IL_0005: ldlen: s_1, pop1, push1
IL_0006: stack_0_i4=(stack_0_i4); //IL_0006: conv.i4: s_1, pop1, push1
IL_0007: stack_1_i4=(8); //IL_0007: ldc.i4.8: s_1, pop0, push1
IL_0008: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_0008: ceq: s_2, pop2, push1
IL_000a: stack_1_i4=(0); //IL_000a: ldc.i4.0: s_1, pop0, push1
IL_000b: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_000b: ceq: s_2, pop2, push1
IL_000d: goto IL_0010; //IL_000d: br.s IL_0010: s_1, pop0, push0
IL_000f: stack_0_i4=(1); //IL_000f: ldc.i4.1: s_0, pop0, push1
IL_0010: var1=stack_0_i4; //IL_0010: stloc.1: s_1, pop1, push0
IL_0011: stack_0_u1=(var1); //IL_0011: ldloc.1: s_0, pop0, push1
IL_0012: if (!(stack_0_u1)) goto IL_0019; //IL_0012: brfalse.s IL_0019: s_1, pop1, push0
IL_0014:
IL_0015: stack_0_i4=(0); //IL_0015: ldc.i4.0: s_0, pop0, push1
IL_0016: var2=stack_0_i4; //IL_0016: stloc.2: s_1, pop1, push0
IL_0017: goto IL_0035; //IL_0017: br.s IL_0035: s_0, pop0, push0
IL_0019: stack_0_ptr=(arg0); //IL_0019: ldarg.0: s_0, pop0, push1
IL_001a: stack_1_i4=(2); //IL_001a: ldc.i4.2: s_1, pop0, push1
IL_001b: stack_0_u1=(((u1*)(stack_0_ptr))[stack_1_i4]); //IL_001b: ldelem.u1: s_2, pop2, push1
IL_001c: stack_1_ptr=(arg0); //IL_001c: ldarg.0: s_1, pop0, push1
IL_001d: stack_2_i4=(3); //IL_001d: ldc.i4.3: s_2, pop0, push1
IL_001e: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_001e: ldelem.u1: s_3, pop2, push1
IL_001f: stack_2_i4=(8); //IL_001f: ldc.i4.8: s_2, pop0, push1
IL_0020: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_0020: shl: s_3, pop2, push1
IL_0021: stack_0_i4=((stack_0_u1)|(stack_1_i4)); //IL_0021: or: s_2, pop2, push1
IL_0022: stack_1_ptr=(arg0); //IL_0022: ldarg.0: s_1, pop0, push1
IL_0023: stack_2_i4=(4); //IL_0023: ldc.i4.4: s_2, pop0, push1
IL_0024: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_0024: ldelem.u1: s_3, pop2, push1
IL_0025: stack_2_i4=(16); //IL_0025: ldc.i4.s 16: s_2, pop0, push1
IL_0027: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_0027: shl: s_3, pop2, push1
IL_0028: stack_0_i4=((stack_0_i4)|(stack_1_i4)); //IL_0028: or: s_2, pop2, push1
IL_0029: stack_1_ptr=(arg0); //IL_0029: ldarg.0: s_1, pop0, push1
IL_002a: stack_2_i4=(5); //IL_002a: ldc.i4.5: s_2, pop0, push1
IL_002b: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_002b: ldelem.u1: s_3, pop2, push1
IL_002c: stack_2_i4=(24); //IL_002c: ldc.i4.s 24: s_2, pop0, push1
IL_002e: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_002e: shl: s_3, pop2, push1
IL_002f: stack_0_i4=((stack_0_i4)|(stack_1_i4)); //IL_002f: or: s_2, pop2, push1
IL_0030: var0=stack_0_i4; //IL_0030: stloc.0: s_1, pop1, push0
IL_0031: stack_0_i4=(var0); //IL_0031: ldloc.0: s_0, pop0, push1
IL_0032: var2=stack_0_i4; //IL_0032: stloc.2: s_1, pop1, push0
IL_0033: goto IL_0035; //IL_0033: br.s IL_0035: s_0, pop0, push0
IL_0035: stack_0_i4=(var2); //IL_0035: ldloc.2: s_0, pop0, push1
IL_0036: return stack_0_i4; //IL_0036: ret: s_1, pop1, push0
}

i4 cfun1(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_0_i4;;
u1 stack_0_u1;;
i4 stack_1_i4;;
ptr stack_1_ptr;;
i4 stack_2_i4;;
u1 stack_1_u1;;
 //local_vars:
i4 var0;
u1 var1;
i4 var2;
 IL_0000:
IL_0001: stack_0_ptr=(arg0); //IL_0001: ldarg.0: s_0, pop0, push1
IL_0002: if (!(stack_0_ptr)) goto IL_000f; //IL_0002: brfalse.s IL_000f: s_1, pop1, push0
IL_0004: stack_0_ptr=(arg0); //IL_0004: ldarg.0: s_0, pop0, push1
IL_0005: stack_0_i4=(*(((i4*)stack_0_ptr)-1)); //IL_0005: ldlen: s_1, pop1, push1
IL_0006: stack_0_i4=(stack_0_i4); //IL_0006: conv.i4: s_1, pop1, push1
IL_0007: stack_1_i4=(7); //IL_0007: ldc.i4.7: s_1, pop0, push1
IL_0008: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_0008: ceq: s_2, pop2, push1
IL_000a: stack_1_i4=(0); //IL_000a: ldc.i4.0: s_1, pop0, push1
IL_000b: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_000b: ceq: s_2, pop2, push1
IL_000d: goto IL_0010; //IL_000d: br.s IL_0010: s_1, pop0, push0
IL_000f: stack_0_i4=(1); //IL_000f: ldc.i4.1: s_0, pop0, push1
IL_0010: var1=stack_0_i4; //IL_0010: stloc.1: s_1, pop1, push0
IL_0011: stack_0_u1=(var1); //IL_0011: ldloc.1: s_0, pop0, push1
IL_0012: if (!(stack_0_u1)) goto IL_0019; //IL_0012: brfalse.s IL_0019: s_1, pop1, push0
IL_0014:
IL_0015: stack_0_i4=(0); //IL_0015: ldc.i4.0: s_0, pop0, push1
IL_0016: var2=stack_0_i4; //IL_0016: stloc.2: s_1, pop1, push0
IL_0017: goto IL_0035; //IL_0017: br.s IL_0035: s_0, pop0, push0
IL_0019: stack_0_ptr=(arg0); //IL_0019: ldarg.0: s_0, pop0, push1
IL_001a: stack_1_i4=(2); //IL_001a: ldc.i4.2: s_1, pop0, push1
IL_001b: stack_0_u1=(((u1*)(stack_0_ptr))[stack_1_i4]); //IL_001b: ldelem.u1: s_2, pop2, push1
IL_001c: stack_1_ptr=(arg0); //IL_001c: ldarg.0: s_1, pop0, push1
IL_001d: stack_2_i4=(3); //IL_001d: ldc.i4.3: s_2, pop0, push1
IL_001e: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_001e: ldelem.u1: s_3, pop2, push1
IL_001f: stack_2_i4=(8); //IL_001f: ldc.i4.8: s_2, pop0, push1
IL_0020: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_0020: shl: s_3, pop2, push1
IL_0021: stack_0_i4=((stack_0_u1)|(stack_1_i4)); //IL_0021: or: s_2, pop2, push1
IL_0022: stack_1_ptr=(arg0); //IL_0022: ldarg.0: s_1, pop0, push1
IL_0023: stack_2_i4=(4); //IL_0023: ldc.i4.4: s_2, pop0, push1
IL_0024: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_0024: ldelem.u1: s_3, pop2, push1
IL_0025: stack_2_i4=(16); //IL_0025: ldc.i4.s 16: s_2, pop0, push1
IL_0027: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_0027: shl: s_3, pop2, push1
IL_0028: stack_0_i4=((stack_0_i4)|(stack_1_i4)); //IL_0028: or: s_2, pop2, push1
IL_0029: stack_1_ptr=(arg0); //IL_0029: ldarg.0: s_1, pop0, push1
IL_002a: stack_2_i4=(5); //IL_002a: ldc.i4.5: s_2, pop0, push1
IL_002b: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_002b: ldelem.u1: s_3, pop2, push1
IL_002c: stack_2_i4=(24); //IL_002c: ldc.i4.s 24: s_2, pop0, push1
IL_002e: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_002e: shl: s_3, pop2, push1
IL_002f: stack_0_i4=((stack_0_i4)|(stack_1_i4)); //IL_002f: or: s_2, pop2, push1
IL_0030: var0=stack_0_i4; //IL_0030: stloc.0: s_1, pop1, push0
IL_0031: stack_0_i4=(var0); //IL_0031: ldloc.0: s_0, pop0, push1
IL_0032: var2=stack_0_i4; //IL_0032: stloc.2: s_1, pop1, push0
IL_0033: goto IL_0035; //IL_0033: br.s IL_0035: s_0, pop0, push0
IL_0035: stack_0_i4=(var2); //IL_0035: ldloc.2: s_0, pop0, push1
IL_0036: return stack_0_i4; //IL_0036: ret: s_1, pop1, push0
}

i4 cfun2(u1* args){
//args:
int argN=0;
 ptr arg0 = *(ptr*)&args[(argN++)*4];
//stack_vars:
ptr stack_0_ptr;;
i4 stack_0_i4;;
u1 stack_0_u1;;
i4 stack_1_i4;;
ptr stack_1_ptr;;
i4 stack_2_i4;;
u1 stack_1_u1;;
 //local_vars:
i4 var0;
u1 var1;
i4 var2;
 IL_0000:
IL_0001: stack_0_ptr=(arg0); //IL_0001: ldarg.0: s_0, pop0, push1
IL_0002: if (!(stack_0_ptr)) goto IL_000f; //IL_0002: brfalse.s IL_000f: s_1, pop1, push0
IL_0004: stack_0_ptr=(arg0); //IL_0004: ldarg.0: s_0, pop0, push1
IL_0005: stack_0_i4=(*(((i4*)stack_0_ptr)-1)); //IL_0005: ldlen: s_1, pop1, push1
IL_0006: stack_0_i4=(stack_0_i4); //IL_0006: conv.i4: s_1, pop1, push1
IL_0007: stack_1_i4=(7); //IL_0007: ldc.i4.7: s_1, pop0, push1
IL_0008: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_0008: ceq: s_2, pop2, push1
IL_000a: stack_1_i4=(0); //IL_000a: ldc.i4.0: s_1, pop0, push1
IL_000b: stack_0_i4=((stack_0_i4)==(stack_1_i4)); //IL_000b: ceq: s_2, pop2, push1
IL_000d: goto IL_0010; //IL_000d: br.s IL_0010: s_1, pop0, push0
IL_000f: stack_0_i4=(1); //IL_000f: ldc.i4.1: s_0, pop0, push1
IL_0010: var1=stack_0_i4; //IL_0010: stloc.1: s_1, pop1, push0
IL_0011: stack_0_u1=(var1); //IL_0011: ldloc.1: s_0, pop0, push1
IL_0012: if (!(stack_0_u1)) goto IL_0019; //IL_0012: brfalse.s IL_0019: s_1, pop1, push0
IL_0014:
IL_0015: stack_0_i4=(0); //IL_0015: ldc.i4.0: s_0, pop0, push1
IL_0016: var2=stack_0_i4; //IL_0016: stloc.2: s_1, pop1, push0
IL_0017: goto IL_0027; //IL_0017: br.s IL_0027: s_0, pop0, push0
IL_0019: stack_0_ptr=(arg0); //IL_0019: ldarg.0: s_0, pop0, push1
IL_001a: stack_1_i4=(0); //IL_001a: ldc.i4.0: s_1, pop0, push1
IL_001b: stack_0_u1=(((u1*)(stack_0_ptr))[stack_1_i4]); //IL_001b: ldelem.u1: s_2, pop2, push1
IL_001c: stack_1_ptr=(arg0); //IL_001c: ldarg.0: s_1, pop0, push1
IL_001d: stack_2_i4=(1); //IL_001d: ldc.i4.1: s_2, pop0, push1
IL_001e: stack_1_u1=(((u1*)(stack_1_ptr))[stack_2_i4]); //IL_001e: ldelem.u1: s_3, pop2, push1
IL_001f: stack_2_i4=(8); //IL_001f: ldc.i4.8: s_2, pop0, push1
IL_0020: stack_1_i4=((stack_1_u1)<<(stack_2_i4)); //IL_0020: shl: s_3, pop2, push1
IL_0021: stack_0_i4=((stack_0_u1)|(stack_1_i4)); //IL_0021: or: s_2, pop2, push1
IL_0022: var0=stack_0_i4; //IL_0022: stloc.0: s_1, pop1, push0
IL_0023: stack_0_i4=(var0); //IL_0023: ldloc.0: s_0, pop0, push1
IL_0024: var2=stack_0_i4; //IL_0024: stloc.2: s_1, pop1, push0
IL_0025: goto IL_0027; //IL_0025: br.s IL_0027: s_0, pop0, push0
IL_0027: stack_0_i4=(var2); //IL_0027: ldloc.2: s_0, pop0, push1
IL_0028: return stack_0_i4; //IL_0028: ret: s_1, pop1, push0
}

