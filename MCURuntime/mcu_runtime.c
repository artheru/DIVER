#ifndef IS_MCU
#include "mcu_runtime.h"
#define INLINE static inline
#else
#include "appl/vm.h"
#include "util/console.h"
#include "appl/states.h"
#undef _DEBUG
#define INLINE __attribute__((always_inline)) static inline
#endif

// allow unsafe strings...
#ifdef _MSC_VER
#define _CRT_SECURE_NO_WARNINGS
#define _CRT_NONSTDC_NO_DEPRECATE

#include <windows.h>
#include <dbghelp.h>

#pragma comment(lib, "dbghelp.lib")
#endif

// the required library
#include <string.h>   // memcpy/memset
#include <stdio.h>    // sprintf
#include <stdlib.h>   // atoi/iota
#include <math.h>     // all
#include <stdint.h>

//hint: all structs are 1 bytes aligned.
#pragma pack(push, 1)

int cur_il_offset;


#ifdef _DEBUG

// debug mode:

// swith on this variable to allow verbose output.
// #define _VERBOSE
// #define DEBUG_COMPILER

#ifdef _VERBOSE
#define DBG printf
#else
#define DBG ;
#endif

// Language-level assertions for interpreter debugging (only active in DEBUG_COMPILER mode)
#ifdef DEBUG_COMPILER
#define ASSERT_LANG(expr, ...) if (!(expr)) { char sprintf_format[100] = { 0 }; sprintf(sprintf_format, __VA_ARGS__); report_error(cur_il_offset, sprintf_format); }
#else
#define ASSERT_LANG(expr, ...) ((void)0)
#endif

#else

// release mode functions.

VAL_OUT(ptr);
#define DBG(...) ;
#define DBG(...) ;
#define WARN(...) ;
#define ASSERT_LANG(expr, ...) ((void)0)

#endif


// Runtime assertions for safety checks (always active)
#define ASSERT_RT(expr, ...) if (!(expr)) { char err_tmp[256]={0}; int sz = snprintf(err_tmp,sizeof(err_tmp),__VA_ARGS__); report_error(cur_il_offset, err_tmp); }

/*
 * memory layout:
 * {                                                                  } is downloaded from Medulla.
 * <mem0>{meta_data|program_descriptor|code|virts|statics_descriptor|}actual statics data...|stack....heap|
 *            -1
 *
 * statics_descriptor: contains cartIO fields and static fields, layout: |length 2B|typeid *n|<static_vals_ptr>|
 *
 * stack(frame):
 * |running_pointer 4B|starting_pointer 4B|evaluation_pointer 4B|args_ptr 4B|vars_ptr 4B|arguments|vars|structs(value_type)|evaluation_stack ...evaluation_stack:(typeid|payload|pop_sz)|...
 * more note on evaluation_stack:(typeid|payload|pop_sz)|... pop_sz = typeid+payload length:1+get_type_length(id)
 *
 * meta_data: |operation interval(in us) 4B|entry_point offset 4B|
 *            |program_descriptor_size 4B|code_chunk_size 4B|statics_descriptor 4B|
 *            |this class-id(create on initialization) 4B|
 *
 * program_discriptor:
 *     cartIO number 2B|
 *     cartIO layout [offset 4B]*number|                            kind: upper? or not?
 *     instanceable_class number 2B|                                  total number of classes
 *     instanceable_class layout [tot_size 2B|n_of_fields 1B] (4)*{N}B|     how many fields?
 *     (class_instance_fields layout typeid 1B*{n_of_fields}B)*{N}|               each field.
 *
 * code_chunk: n_of_methods 2B|method_index_table:(meta offset 4B|code offset 4B)*N|{methods:{(var number 2B|var typeid*N|arg number 2B|arg typeid*N|code)} * N}
 *                                          offset relative to the first method.
 *
 * virts: used for virtual method call(callvirt).
 *     n_of_methods 2B|offsets*N 2B|(impled_cls_n 2B|(clsid 2B|methodid 2B)*n)*N
*/

typedef void (*builtin_method_t)(uchar** eptr);

// Number of built-in methods
#define NUM_BUILTIN_METHODS 256
static int bn = 0;  // Builtin number counter

// Array of built-in method function pointers
builtin_method_t builtin_methods[NUM_BUILTIN_METHODS];


#define ReadInt *((int*)ptr); ptr += 4
#define ReadShort *((short*)ptr); ptr += 2
#define ReadString (char*)(ptr + 2); ptr += *((unsigned short*)ptr) + 2
#define ReadBool *((bool*)ptr); ptr += 1
#define ReadByte *((unsigned char*)ptr); ptr += 1
// #define ReadFloat *((float*)ptr); ptr += 4
#define ReadArr(type, len) (type*)ptr; ptr += len * sizeof(type);

struct method_index
{
	int meta_offset;
	int code_offset;
};
struct method_index* methods_table;
uchar* method_detail_pointer;

#define STACK_STRIDE 8
struct stack_frame_header
{
	short method_id; short stack_depth;
	uchar* PC, * entry_il, * evaluation_pointer, * args, * vars, * evaluation_st_ptr;
	int max_stack;
};
struct stack_frame_header* stack_ptr[32]; // maximum 32 depth.
struct stack_frame_header* stack0;
int new_stack_depth = 0;

int il_cnt = 0;

#define maximum_IO_N 1024
unsigned int cart_IO_stored[maximum_IO_N / 32]; // 32*32bit, 1024 slot for cart_IO.

#define SET_CART_IO_TOUCHED(io_id) (cart_IO_stored[(io_id) / 32] |= (1U << ((io_id) % 32)))

uchar* program_desc_ptr, * code_ptr, * virt_ptr, * statics_desc_ptr, * statics_val_ptr;
struct
{
	unsigned short tot_size;
	uchar n_of_fields;
	int layout_offset;
} *instanceable_class_layout_ptr;
uchar* instanceable_class_per_layout_ptr;
int* cartIO_layout_ptr;

struct per_field
{
	uchar typeid;
	unsigned short offset;
	short aux;
};

uchar* mem0;
uchar* heap_tail;
int heap_newobj_id = 1;
struct heap_obj_slot
{
	uchar* pointer;
	short new_id; // only used on cleanup.
} heap_obj[1024];
// reference id 0 is for nullpointer.
// `this` for entry method, aka, operation(int i), is always reference id 1.

// heap management:
// 1. heap grow from tail to head. only alloc no free.
// 2. after each vm_run, do a cleanup:
//   1> mark all referenced heap object.
//   2> re-assign id for alive objects;
//   3> from tail to head(heap id low->hi), move object chunk to end. (to move a object, we use a "reversed" memcpy, in order the moving distance
//      is less than the chunk size). note the object can only be moved tail-wise, so it works.

int entry_method_id;
int init_method_id;
int ladderlogic_this_refid;
int ladderlogic_this_clsid;
int statics_amount, cartIO_N, instanceable_class_N;

int methods_N, vmethods_N;

int snapshot_state = 0;

// MCU-device input/output buffer, not demanding high speed memory:
#define BUF_SZ 8192
#define SLOT_NUMBER 256

uchar IO_bufferA[BUF_SZ], IO_bufferB[BUF_SZ];
short sorted_slots[SLOT_NUMBER];

// layout:  indexier_len|[indexier_type 1B|dummy 1B|len 2B|aux1 4B|aux2 4B]...16Bper slot, 2048B slot size.
struct io_slot
{
	union {
		struct {
			short aux1;
			uchar aux0;
			uchar type;
		};
		unsigned int sortable;
	};
	unsigned short len;
	int offset;
};
struct io_buf
{
	int N_slots; int offset;
	struct io_slot slots[SLOT_NUMBER];
	uchar payload;
};
struct io_buf* writing_buf = IO_bufferA, * processing_buf = IO_bufferB;

#define As(What, TType) (*(TType*)(What))

struct array_val
{
	uchar header; //ArrayHeader
	uchar typeid;
	int len;
	uchar payload;
};
struct string_val
{
	uchar header; //StringHeader
	unsigned short str_len;
	uchar payload;
};
struct object_val
{
	uchar header; //ObjectHeader
	unsigned short clsid;
	uchar payload;
};

/*
 *  STACK value layout:
 *  |typeid 1B|aux 3B|payload nB|.
 *
 */

#define  ArrayHeader 11
#define ArrayHeaderSize 6

#define  StringHeader 12
#define StringHeaderSize 3

#define  ObjectHeader 13
#define ObjectHeaderSize 3

#define  Boolean 0
#define  Byte 1
#define  SByte 2
#define  Int16 4
#define  UInt16 5
#define  Int32 6
#define  UInt32 7
#define  Single 8

#define  MethodPointer 14
struct method_pointer
{
	char type;
	short id;
	char dummy;
};

#define  Address 15
#define  ReferenceID 16 
#define  JumpAddress 17

#define  BoxedObject 18 
#define  Metadata 19 

INLINE uchar get_type_sz(uchar typeid)
{
	// maybe use math to do this?
	switch (typeid)
	{
	case 0:  return 1;  // Boolean 
	case 1:  return 1;  // Byte
	case 2:  return 1;  // SByte
	case 3:  return 2;  // Char
	case 4:  return 2;  // Int16
	case 5:  return 2;  // UInt16
	case 6:  return 4;  // Int32
	case 7:  return 4;  // UInt32  
	case 8:  return 4;  // Single 
		//(U)Int64 no support.

		// on heap.
		// case 11: return 5; // *ArrayHeader     |byte typeid|int len|value|value|value|...//value in array still have a typeid header, very redundant...
		// case 12: return 2; // *String          |short len| ...
		// case 13: return 2; // *ObjectHeader    |short clsid| ...

	case 14: return 4; // *MethodPointer   |byte type|short id|dummy| // type:0 buildin, type:1 custom.
	case 15: return 5; // &Address |actual_type|address|
	case 16: return 4; // %ReferenceID (ReferenceID)
	case 17: return 4; // &JumpID
	case 18: return 5; // boxed object: |actual_type|payload|
	default:
		ASSERT_LANG(0, "invalid typeid %d", typeid);
	}
}
uchar get_val_sz(uchar typeid)
{
	return get_type_sz(typeid) + 1;
}

uchar* builtin_cls[];
int builtin_arg0; //this pointer for builtin class ctor.

// use heap_newobj_id-1 to get obj_id.
int newobj(int clsid)
{
	ASSERT_LANG(clsid != -1, "bad clsid:-1");
	int reference_id = heap_newobj_id;
	heap_newobj_id++;
	uchar* tail = reference_id == 1 ? heap_tail : heap_obj[reference_id - 1].pointer;
	short is_builtin = (clsid & 0xf000);
	int mysz = ( is_builtin ? 
		builtin_cls[(short)(clsid - 0xf000)][0] * 5 : // todo: this require builtin class to be all 5 padding fields.
		instanceable_class_layout_ptr[clsid].tot_size) + ObjectHeaderSize;
	struct object_val* my_ptr = tail - mysz;
	if (new_stack_depth > 0 && (uchar*)my_ptr < stack_ptr[new_stack_depth - 1]->evaluation_pointer)
		ASSERT_RT(0, "Out of memory allocating %d bytes for obj(%d)", mysz, clsid);
	heap_obj[reference_id] = (struct heap_obj_slot){ .pointer = my_ptr, };
	// initialize:
	my_ptr->header = ObjectHeader;
	my_ptr->clsid = clsid;

	// Zero only necessary fields - most will be initialized explicitly
	int payload_size = mysz - ObjectHeaderSize;
	if (payload_size > 0) {
		memset(&my_ptr->payload, 0, payload_size);
	}

	if (is_builtin)
	{
		uchar* ftype = builtin_cls[(short)(clsid - 0xf000)];
		uchar* ptr = &my_ptr->payload;
		for (int j = 0; j < *ftype; ++j)
		{
			*ptr = ftype[j + 1];
			ptr += 5;
		}
		// instantiate will be done in builtin ctor functions... we just set type here.
	}
	else {
		struct per_field* my_layout = instanceable_class_per_layout_ptr + instanceable_class_layout_ptr[clsid].layout_offset;

		for (int i = 0; i < instanceable_class_layout_ptr[clsid].n_of_fields; ++i) {
			(&my_ptr->payload)[my_layout[i].offset] = my_layout[i].typeid;
			if (my_layout[i].aux != -1 && my_layout[i].typeid == ReferenceID) //struct need to instantiate immediately.
			{
				*((int*)(&my_ptr->payload + my_layout[i].offset + 1)) = newobj(my_layout[i].aux);
			}
		}
	}

	DBG("created obj_%d(cls_%d) @ %x\n", reference_id, clsid, my_ptr);

	return reference_id;
}

int newstr(short len, uchar* src)
{
	int reference_id = heap_newobj_id;
	uchar* tail = heap_newobj_id == 1 ? heap_tail : heap_obj[heap_newobj_id - 1].pointer;
	int mysz = len + StringHeaderSize + 1;
	struct string_val* my_ptr = tail - mysz;
	if (new_stack_depth > 0 && (uchar*)my_ptr < stack_ptr[new_stack_depth - 1]->evaluation_pointer)
		ASSERT_RT(0, "Out of memory allocating %d bytes for str[%d]", mysz, len);
	heap_obj[reference_id] = (struct heap_obj_slot){ .pointer = my_ptr, };
	// initialize:
	my_ptr->header = StringHeader;
	my_ptr->str_len = len;
	memcpy(&my_ptr->payload, src, len);
	(&my_ptr->payload)[len] = 0; // trailing zero.

	DBG("created obj_%d string `%s`(len=%d) @ %x\n", heap_newobj_id, &(my_ptr->payload), len, my_ptr);
	heap_newobj_id++;
	return reference_id;
}

int newarr(short len, uchar type_id)
{
	int reference_id = heap_newobj_id;
	uchar* tail = heap_newobj_id == 1 ? heap_tail : heap_obj[heap_newobj_id - 1].pointer;
	int elemSz = get_type_sz(type_id); //no header.
	int mysz = elemSz * len + ArrayHeaderSize;
	struct array_val* my_ptr = tail - mysz;
	if (new_stack_depth > 0 && (uchar*)my_ptr < stack_ptr[new_stack_depth - 1]->evaluation_pointer)
		ASSERT_RT(0, "Out of memory allocating %dB for arr[%d](%d)", mysz, len, type_id);
	heap_obj[reference_id] = (struct heap_obj_slot){ .pointer = my_ptr, };

	// initialize:
	my_ptr->header = ArrayHeader;
	my_ptr->typeid = type_id;
	my_ptr->len = len;

	// Zero payload only for reference types (to ensure null refs)
	if (type_id == ReferenceID) {
		memset(&my_ptr->payload, 0, len * get_type_sz(type_id));
	}

	DBG("created obj_%d array (type_%d)x%d and initialized @ %x\n", heap_newobj_id, type_id, len, my_ptr);
	heap_newobj_id++;
	return reference_id;
}

void parse_statics()
{
	uchar* ptr = statics_desc_ptr;
	statics_amount = ReadShort;
	uchar* ptr_s = statics_val_ptr;
	for (int i = 0; i < statics_amount; ++i) {
		uchar typeid = ReadByte;
		short auxid = ReadShort;
		*ptr_s = typeid;
		if (typeid == ReferenceID && auxid >= 0) {
			DBG("init static var_%d, cls_%d: ", i, auxid);
			*(int*)(ptr_s + 1) = newobj(auxid);
		}
		else memset(ptr_s + 1, 0, get_type_sz(typeid));
		ptr_s += get_val_sz(typeid);
	}
	stack0 = ptr_s;
}

void parse_program_desc()
{
	uchar* ptr = program_desc_ptr;
	cartIO_N = ReadShort;
	cartIO_layout_ptr = ptr;
	ptr += cartIO_N * 4; //cart IO offset layout.
	instanceable_class_N = ReadShort;
	instanceable_class_layout_ptr = ptr;
	instanceable_class_per_layout_ptr = ((uchar*)instanceable_class_layout_ptr) + 7 * instanceable_class_N;
}

void parse_methods()
{
	uchar* ptr = code_ptr;
	methods_N = ReadShort;
	methods_table = ptr;
	method_detail_pointer = &methods_table[methods_N];
}

uchar* virt_table;
void parse_virt_methods()
{
	uchar* ptr = virt_ptr;
	vmethods_N = ReadShort;
	virt_table = ptr + vmethods_N * 2;
}


int get_virt_method_actual_methodID(int vmethod_id, int cls_id)
{
	uchar* ptr = virt_table + *((short*)(virt_ptr + 2) + vmethod_id);
	uchar ncls = *ptr;
	uchar paramCnt = *(ptr + 1);
	struct { short clsid; short methodid; } *vm_s = ptr + 2;
	for (int i = 0; i < ncls; ++i, vm_s++)
		if (vm_s->clsid == cls_id) return vm_s->methodid;
	ASSERT_LANG(0, "Cannot find vmethod %d for type %d", vmethod_id, cls_id);
}
void setup_builtin_methods();

int iterations = 0;
void vm_push_stack(int method_id, int new_obj_id, uchar** reptr);
void clean_up();

void mark_object(int obj_id);

int vm_set_program(uchar* vm_memory, int vm_memory_size)
{
	setup_builtin_methods();

	heap_newobj_id = 1;
	ladderlogic_this_refid = 0;
	uchar* ptr = mem0 = vm_memory;
	auto interval = ReadInt;
	entry_method_id = ReadInt;
	init_method_id = ReadInt;
	auto program_desc_sz = ReadInt;
	auto code_chunk_sz = ReadInt;
	auto virt_chunk_sz = ReadInt;
	auto static_desc_sz = ReadInt;
	ladderlogic_this_clsid = ReadInt;

	program_desc_ptr = ptr;
	code_ptr = program_desc_ptr + program_desc_sz;
	virt_ptr = code_ptr + code_chunk_sz;
	statics_desc_ptr = virt_ptr + virt_chunk_sz;
	statics_val_ptr = statics_desc_ptr + static_desc_sz + static_desc_sz;

	heap_tail = vm_memory + vm_memory_size;

	parse_program_desc();
	parse_methods();
	parse_virt_methods();


	DBG("interval=%d, nstatics=%d, this_clsid=%d\n", interval, statics_amount, ladderlogic_this_clsid);
	heap_obj[0] = (struct heap_obj_slot){ .pointer = -1, .new_id = -0xF };
	ladderlogic_this_refid = newobj(ladderlogic_this_clsid);

	// parse statics desc to get stack0 ptr.
	parse_statics();
	if (init_method_id >= 0) {
		struct stack_frame_header* root_frame = (struct stack_frame_header*)stack0;
		memset(root_frame, 0, sizeof(*root_frame));
		root_frame->evaluation_pointer = (uchar*)(root_frame + 1);
		root_frame->evaluation_st_ptr = root_frame->evaluation_pointer;
		stack_ptr[0] = root_frame;
		new_stack_depth = 1;
		uchar* caller_eptr = root_frame->evaluation_pointer;
		vm_push_stack(init_method_id, ladderlogic_this_refid, &caller_eptr);
		new_stack_depth = 0;
		stack_ptr[0] = NULL;
		clean_up();
	}

	iterations = 0;

	processing_buf->offset = writing_buf->offset = 0;
	processing_buf->N_slots = writing_buf->N_slots = 0;

	return interval;
}



#define HEAP_WRITE_INT(val) *heap=Int32; As(heap+1, int)=val; heap+=get_val_sz(Int32);
#define HEAP_WRITE_REFERENCEID(val) *heap=ReferenceID; As(heap+1, int)=val; heap+=get_val_sz(ReferenceID);

// PUSH - optimized to avoid unnecessary memset
#define PUSH_STACK_INT8(val) *eptr = SByte; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT8(val) *eptr = Byte; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT16(val) *eptr = Int16; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT16(val) *eptr = UInt16; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT(val) *eptr = Int32; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT(val) *eptr = UInt32; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;

// now our stack has eptr+1 8B aligned.
#define PUSH_STACK_FLOAT_D(val) *eptr = Single; As(eptr + 1, float) = (val); ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_FLOAT_M(val) { *eptr = Single; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE; }

#define PUSH_STACK_METHODHANDLER(val) *eptr = MethodPointer; As(eptr + 1, struct method_pointer) = (val); eptr+=STACK_STRIDE;

// not on reference id: it's heap object ID, not address!!!
#define PUSH_STACK_REFERENCEID(val) *eptr = ReferenceID; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;

// address is starting at mem0;
#define PUSH_STACK_ADDRESS(val, typeid) *eptr = Address; As(eptr + 1, int) = (int)(val-mem0); *(eptr+5)=typeid; eptr+=STACK_STRIDE;
// typed addr is always on stack.
#define TypedAddrAsValPtr(what) (mem0+As((what)+1, int))
#define TypedAddrGetType(what) (*(((uchar*)what)+5))

// push stack indirect use mem0+ address.
// simply copy 2 ints.
#define PUSH_STACK_INDIRECT(addr) {*(int*)eptr=*(int*)addr; ((int*)eptr)[1]=((int*)addr)[1]; eptr+=8;}

#define POP {eptr-=STACK_STRIDE;\
    if (eptr < my_stack->evaluation_st_ptr) {\
        ASSERT_LANG(0, "POP underflow: method=%d depth=%d eval_ptr=%p st_ptr=%p\n", my_stack->method_id, my_stack->stack_depth, (void*)eptr, (void*)my_stack->evaluation_st_ptr);\
    } }


#define CPYVAL(dst,src,type) {\
	switch (type){ \
case 0:case 1:case 2: (dst)[0] = (src)[0]; break; \
case 3:case 4:case 5: ((short*)(dst))[0] = ((short*)(src))[0]; break; \
default: ((int*)(dst))[0] = ((int*)(src))[0]; break; \
	}}

void copy_val(uchar* dst, uchar* src)
{
	switch (*dst)
	{
	case 0:
		dst[1] = src[1]; return;
	case 1:
	case 2:
		switch (*src)
		{
		case 0: dst[1] = src[1]; DBG("b->i1\n"); return;
		case 1:case 2:dst[1] = src[1]; return;
		case 3:case 4:case 5:case 6:case 7: dst[1] = src[1]; DBG("i2+ ->i1\n"); return;
		default:
			ASSERT_LANG(0, "invalid i1 value copy from type_%d", *src);
		}
	case 3:
	case 4:
	case 5:
		switch (*src)
		{
		case 1: *(int16_t*)(dst + 1) = src[1];
			DBG("u1->iu2\n");
			return;
		case 2: *(int16_t*)(dst + 1) = ((int8_t*)src)[1];
			DBG("i1->iu2\n");
			return;
		case 3:
		case 4:
		case 5:
		case 6:
		case 7: *(int16_t*)(dst + 1) = *(int16_t*)(src + 1);
			return;
		}
		ASSERT_LANG(0, "invalid i2 value copy from type_%d", *src);

	case 6:
	case 7:
		switch (*src)
		{
		case 1:
			*(int32_t*)(dst + 1) = src[1];
			DBG("u1->iu4\n");
		case 2:
			*(int32_t*)(dst + 1) = ((int8_t*)src)[1];
			DBG("i1->iu4\n");
			return;
		case 3:
		case 4: *(int32_t*)(dst + 1) = *(int16_t*)(src + 1);
			DBG("i2->iu4\n");
		case 5: *(int32_t*)(dst + 1) = *(uint16_t*)(src + 1);
			DBG("u2->iu4\n");
			return;
		case 6:
		case 7: *(int32_t*)(dst + 1) = *(int32_t*)(src + 1);
			return;
		}
		ASSERT_LANG(0, "invalid i4 value copy from type_%d", *src);

	case 8:
		if (*src == 8)
		{
			// just use int copy.
			*(int*)(dst + 1) = *(int*)(src + 1);
			return;
		}
		else
		{
			ASSERT_LANG(0, "invalid r4 value copy from type_%d", *src);
		}
	case ReferenceID:
		switch (*src)
		{
		case ReferenceID:
			*(int32_t*)(dst + 1) = *(int32_t*)(src + 1);
			return;
		case JumpAddress:
			DBG("case of copy from JMP to REFID\n");
			// store a struct to heap object. ok, we create copy the object
			struct object_val* obj_src = TypedAddrAsValPtr(src);
			int refid = newobj(obj_src->clsid);
			struct object_val* obj_dst = heap_obj[refid].pointer;
			memcpy(obj_dst, obj_src, instanceable_class_layout_ptr[obj_src->clsid].tot_size + ObjectHeaderSize);
			As(dst + 1, int) = refid;
			return;
		}
		ASSERT_LANG(0, "invalid ref value copy from type_%d", *src);
	case JumpAddress:
		struct object_val* obj_dst = TypedAddrAsValPtr(dst);
		struct object_val* obj_src = 0;
		switch (*src)
		{
		case ReferenceID:
			DBG("case of copy from RefID to JMP\n");
			int ref_id = As(src + 1, int);
			ASSERT_RT(ref_id != 0, "Null reference assignment");
				obj_src = heap_obj[ref_id].pointer;
			break;
		case JumpAddress:
			obj_src = TypedAddrAsValPtr(src);
			break;
		default:
			ASSERT_LANG(0, "invalid struct ja value copy from type_%d", *src);
		}
		memcpy(obj_dst, obj_src, instanceable_class_layout_ptr[obj_src->clsid].tot_size + ObjectHeaderSize);
		return;
	case Address:
		//just copy.
		*(int*)(dst + 1) = *(int*)(src + 1);
		*(dst + 5) = *(src + 5);
		return;
	default:
		ASSERT_LANG(0, "invalid copy dst type_%d", *dst);
	}
}


void vm_push_stack(int method_id, int new_obj_id, uchar** reptr)
{
	ASSERT_LANG(method_id < methods_N, "Bad method id_%d>%d", method_id, methods_N);

	int my_stack_depth = new_stack_depth;
	new_stack_depth += 1;
	struct stack_frame_header* my_stack = my_stack_depth == 0 ? stack0 : stack_ptr[my_stack_depth - 1]->evaluation_pointer;
	stack_ptr[my_stack_depth] = my_stack;
	DBG("vm_push_stack: method=%d depth=%d new_obj=%d\n", method_id, my_stack_depth, new_obj_id);
	uchar* st_ptr = method_detail_pointer + methods_table[method_id].code_offset;
	*my_stack = (struct stack_frame_header){
		.method_id = method_id,
		.stack_depth = my_stack_depth,
		.PC = st_ptr,
		.entry_il = st_ptr, };

	// initialize args and vars by method meta.
	uchar* ptr = method_detail_pointer + methods_table[method_id].meta_offset;
	uchar* sptr = my_stack + 1;

	// retval:
	uchar expected_ret_type = ReadByte;
	short expected_ret_clsid = ReadShort;

	// put args:
	my_stack->args = sptr;
	short n_args = ReadShort;
	DBG("vm_push_stack: method=%d n_args=%d\n", method_id, n_args);

	short cls_id[16];
	uchar* auxptr[16];
	int cpy_obj_id[16];

	int aux_init = 0;

	if (my_stack_depth == 0)
	{
		uchar typeid0 = ReadByte;
		short aux0 = ReadShort;
		uchar typeid1 = ReadByte;
		short aux1 = ReadShort;
		if (typeid0 != 16 || typeid1 != 6 || n_args != 2)
		{
			ASSERT_LANG(0, "Entry Method must be 'void Operation(int i)'");
		}
		//reference_id = 1. if it's the root method, `this` is always obj1.
		*sptr = ReferenceID; As(sptr + 1, int) = ladderlogic_this_refid; sptr += 1 + get_type_sz(ReferenceID);
		*sptr = Int32; As(sptr + 1, int) = iterations; sptr += 1 + get_type_sz(Int32);
	}
	else {
		// validate previous stack frame.
		struct stack_frame_header* caller_stack = stack_ptr[my_stack_depth - 1];
		uchar* eptr = caller_stack->evaluation_pointer;

		for (int i = n_args - 1; i >= (new_obj_id > 0 ? 1 : 0); --i)
		{
			eptr -= STACK_STRIDE;
			if (eptr < caller_stack->evaluation_st_ptr)
			{
				ASSERT_LANG(0, "vm_push_stack underflow: method=%d n_args=%d i=%d caller_depth=%d eval_ptr=%p st_ptr=%p\n", method_id, n_args, i, my_stack_depth, (void*)eptr, (void*)caller_stack->evaluation_st_ptr);
			}
		}

		uchar* septr = eptr; // stack vals pointer.

		if (new_obj_id > 0)
		{
			uchar this_typeid = ReadByte;
			short aux = ReadShort;
			ASSERT_LANG(this_typeid == ReferenceID, "newobj call but this pointer is %d", this_typeid);
			*sptr = ReferenceID;
			As(sptr + 1, int) = new_obj_id;
			sptr += get_val_sz(ReferenceID);
		}

		for (int i = (new_obj_id > 0 ? 1 : 0); i < n_args; ++i)
		{
			uchar typeid = ReadByte;
			short aux = ReadShort;
			uchar sz = get_val_sz(typeid);
			if (typeid == JumpAddress) {
				ASSERT_LANG(aux != -1, "jump address but bad instantiate class");
				cls_id[aux_init] = aux;
				auxptr[aux_init] = sptr;
				if (*septr == ReferenceID)
				{
					cpy_obj_id[aux_init] = As(septr + 1, int);
				}
				else
				{
					ASSERT_LANG(0, "not supported arg push for jumpaddress from type_%d", *septr);
				}
				sptr[0] = JumpAddress;
				sptr += sz;

				septr += STACK_STRIDE;
				aux_init++;
				continue;
			}

			sptr[0] = typeid;
			copy_val(sptr, septr);
			sptr += sz;
			septr += STACK_STRIDE;
		}

		caller_stack->evaluation_pointer = eptr;
		if (reptr) *reptr = eptr; //pop arguments for previous stack.
	}
	// initialize vars:
	my_stack->vars = sptr;
	short n_vars = ReadShort;

	for (int i = (new_obj_id >= 0 ? 1 : 0); i < n_vars; ++i)
	{
		uchar typeid = ReadByte;
		short aux = ReadShort;
		if (typeid == JumpAddress) {
			ASSERT_LANG(aux != -1, "jump address but bad instantiate class");
			cls_id[aux_init] = aux;
			auxptr[aux_init] = sptr;
			cpy_obj_id[aux_init] = 0; // no copy, just init.
			aux_init++;
		}
		*sptr = typeid;
		sptr++;
		int len = get_type_sz(typeid);
		for (int j = 0; j < len; ++j, ++sptr)
			*sptr = 0;
	}

	// aux init:
	for (int i = 0; i < aux_init; ++i)
	{
		short clsid = cls_id[i];
		int mysz = instanceable_class_layout_ptr[clsid].tot_size + ObjectHeaderSize;
		struct object_val* my_ptr = sptr;
		my_ptr->header = ObjectHeader;
		my_ptr->clsid = clsid;
		if (cpy_obj_id[i] > 0)
		{
			struct object_val* obj_ptr = heap_obj[cpy_obj_id[i]].pointer;
			ASSERT_LANG(obj_ptr->clsid == clsid, "copy from bad class_%d, expected cls_%d", obj_ptr->clsid, clsid);
			memcpy(&my_ptr->payload, &obj_ptr->payload, instanceable_class_layout_ptr[clsid].tot_size);
			DBG("struct copy init (cls_%d) on %d from obj_%d\n", clsid, i, cpy_obj_id[i]);
		}
		else {
			struct per_field* my_layout = instanceable_class_per_layout_ptr + instanceable_class_layout_ptr[clsid].layout_offset;
			for (int i = 0; i < instanceable_class_layout_ptr[clsid].n_of_fields; ++i)
				(&my_ptr->payload)[my_layout[i].offset] = my_layout[i].typeid;

			DBG("struct zero init (cls_%d) on %d\n", clsid, i);
		}

		As(auxptr[i] + 1, int) = (int)(sptr - mem0); //write offset as address.
		sptr += mysz;
	}

	// evaluation stack is predetermined max_stack.
	my_stack->max_stack = ReadInt;
	my_stack->evaluation_st_ptr = my_stack->evaluation_pointer = (((int)(sptr - mem0 + 3) >> 2) << 2) + mem0 + 3;

	DBG(">>> Stack Custom Method %d, pop %d vals\n", method_id, n_args);

	// start running:
	while (1)
	{
		uchar* ptr = my_stack->PC; // pointer to program code
		cur_il_offset = ptr - mem0;
		uchar* eptr = my_stack->evaluation_pointer; // pointer to evaluation stack.

		uchar ic = ReadByte;
		il_cnt += 1;

		DBG("ic=%X(%d,off%d): ", ic, il_cnt, cur_il_offset);

		switch (ic)
		{
		case 0x00:
			DBG
			("IL_Nop\n");
			break;
		case 0x01:
			DBG
			("IL_Break\n");
			break;

		case 0x02:
		{
			// load argument onto stack:
			unsigned short offset = ReadShort;
			PUSH_STACK_INDIRECT(&my_stack->args[offset]);
			DBG("IL_Ldarg @%d, typeid=%d\n", offset, my_stack->args[offset]);
			break;
		}
		case 0x03:
		{
			unsigned short offset = ReadShort;
			uchar* addr = my_stack->args + offset;

			// PUSH
			PUSH_STACK_ADDRESS(addr + 1, *addr);

			DBG("IL_Ldarga referencing offset %d, type %d\n", offset, *addr);
			break;
		}
		case 0x04:
		{
			unsigned short offset = ReadShort;
			uchar* addr = my_stack->args + offset;
			POP;
			copy_val(addr, eptr);
			DBG
			("IL_Starg type_%d -> arg@%d(type_%d)\n", *addr, offset, *eptr);
			break;
		}
		case 0x06:
		{
			unsigned short var_offset = ReadShort;
			PUSH_STACK_INDIRECT(&my_stack->vars[var_offset]);
			DBG
			("IL_Ldloc var@%d(type_%d)\n", var_offset, my_stack->vars[var_offset]);
			break;
		}
		case 0x0A:
		{
			uchar typeid = ReadByte; // actually not necessary;
			unsigned short offset = ReadShort;
			uchar* addr = my_stack->vars + offset;
			// POP
			POP;
			copy_val(addr, eptr);
			DBG
			("IL_Stloc from stack(type_%d) -> var@%d(type_%d)\n", typeid, offset, typeid);
			break;
		}
		case 0x0B:
		{
			unsigned short var_offset = ReadShort;
			uchar* addr = my_stack->vars + var_offset;
			PUSH_STACK_ADDRESS(addr + 1, *addr);
			DBG
			("IL_Ldloca var_offset:%d, type%d\n", var_offset, *addr);
			break;
		}
		case 0x15:
		{
			uchar ldc_typeid = ReadByte;
			// only int/float/null type uses 0x15.
			switch (ldc_typeid)
			{
			case 6:
			{
				int val = ReadInt;
				PUSH_STACK_INT(val);
				DBG
				("IL_Ldc_I4 %d\n", val);
				break;
			}
			case 8:
			{
				// float need dedicate treatment.
				int val = ReadInt;
				PUSH_STACK_FLOAT_M(val);

				DBG
				("IL_Ldc_R4 %f\n", val);
				break;
			}
			case ReferenceID:
			{
				PUSH_STACK_REFERENCEID(0);
				DBG
				("ldnull \n");
				break;
			}
			}
			break;
		}
		case 0x16: // heap object loading. Ldstr or Newarr
		{
			uchar typeid = ReadByte;
			if (typeid == StringHeader)
			{
				short len = ReadShort;
				// Create new object
				int id = newstr(len, ptr);
				PUSH_STACK_REFERENCEID(id);
				// Implement string creation logic here
				DBG
				("IL_Ldstr\n", len); // infomation is output in newstr.
				ptr += len;
			}
			else if (typeid == ArrayHeader)
			{
				uchar elem_typeid = ReadByte;
				POP;
				ASSERT_LANG(*eptr == Int32, "Stack value is not int32 for IL_Newarr");
				int len = As(eptr + 1, int);

				int id = newarr(len, elem_typeid);
				PUSH_STACK_REFERENCEID(id);

				if (elem_typeid == ReferenceID)
				{
					short aux = -1;
					aux = ReadShort;
					if (aux >= 0) //struct array.
					{
						struct array_val* my_ptr = heap_obj[id].pointer;
						for (int i = 0; i < len; ++i)
							*(int*)&(&my_ptr->payload)[get_type_sz(ReferenceID) * i] = newobj(aux);
					}
				}
				// Implement array creation logic here
				DBG
				("IL_Newarr elem_type: %d, len=%d\n", elem_typeid, len);
			}
			break;
		}
		case 0x23:
		{
			uchar* teptr = eptr - 8;
			PUSH_STACK_INDIRECT(teptr);
			DBG("IL_Dup\n");
			break;
		}
		case 0x24:
		{
			POP
				DBG
				("IL_Pop\n");
			break;
		}
		case 0x25:
		{
			unsigned short method_id = ReadShort;
			// Implement method jump logic here
			DBG
			("IL_Jmp to method %d\n", method_id);
			break;
		}
		case 0x26: //IL_Ret
		{
			// Return from method
			if (eptr > my_stack->evaluation_st_ptr)
			{
				POP;

				// PUSH to previous stack.
				if (my_stack_depth > 0) {
					**(int**)reptr = *(int*)eptr;
					(*(int**)reptr)[1] = ((int*)eptr)[1];
					*reptr += STACK_STRIDE;
				}

				DBG("IL_Ret type_%d, val=%x\n", *eptr, *(eptr + 1));
			}
			else
			{
				DBG("IL_Ret void ");
			}
			goto exited;
		}

		case 0x27: // Br_S
		case 0x34: // Br
		{
			short offset = ReadShort;
			ptr = my_stack->entry_il + offset;
			DBG
			("IL_Branch to offset %d, \n", offset);
			break;
		}

		// Mono-operand branch
		case 0x28: // Brfalse_S
		case 0x35: // Brfalse
		case 0x29: // Brtrue_S
		case 0x36: // Brtrue
		{
			short offset = ReadShort;
			POP;
			uchar* val1p = eptr;
			ASSERT_LANG(*val1p <= 7 || *val1p == ReferenceID, "not supported branch operand type");
			uchar val1 = eptr[1];
			int condition;
			switch (ic)
			{
			case 0x28:
			case 0x35: condition = (val1 == 0);
				break; // Brfalse
			case 0x29:
			case 0x36: condition = (val1 != 0);
				break; // Brtrue
			}
			if (condition)
			{
				ptr = my_stack->entry_il + offset;
				DBG
				("IL_Branch type 0x%02X, offset %d, condition true\n", ic, offset);
			}
			else
			{
				DBG
				("IL_Branch type 0x%02X, offset %d, condition false\n", ic, offset);
			}
			break;
		}

		// Bioperand branch
		case 0x2A: // Beq_S
		case 0x2B: // Bge_S
		case 0x2C: // Bgt_S
		case 0x2D: // Ble_S
		case 0x2E: // Blt_S
		case 0x2F: // Bne_Un_S
		case 0x30: // Bge_Un_S
		case 0x31: // Bgt_Un_S
		case 0x32: // Ble_Un_S
		case 0x33: // Blt_Un_S
		case 0x37: // Beq
		case 0x38: // Bge
		case 0x39: // Bgt
		case 0x3A: // Ble
		case 0x3B: // Blt
		case 0x3C: // Bne_Un
		case 0x3D: // Bge_Un
		case 0x3E: // Bgt_Un
		case 0x3F: // Ble_Un
		case 0x40: // Blt_Un
		{
			short offset = ReadShort;

			char condition = 1;

			int val1, val2;
			float fval1, fval2;
			char is_float = 0;

			POP;
			uchar* val2p = eptr;
			POP;
			uchar* val1p = eptr;
			ASSERT_LANG(*val2p == *val1p, "comparison operands not same type");

			switch (*val1p)
			{
			case Int32:
			case UInt32:
				val2 = As(val2p + 1, int);
				val1 = As(val1p + 1, int);
				break;
			case Int16:
			case UInt16:
				val2 = As(val2p + 1, short);
				val1 = As(val1p + 1, short);
				break;
			case Byte:
			case SByte:
				val2 = As(val2p + 1, char);
				val1 = As(val1p + 1, char);
				break;
			case Single:
				is_float = 1;
				val2 = As(val2p + 1, int);
				val1 = As(val1p + 1, int);
				fval2 = As(&val2, float);
				fval1 = As(&val1, float);
				break;
			default:
				ASSERT_LANG(0, "Unsupported type for comparison");
			}

			switch (ic) //?
			{
			case 0x2A:
			case 0x37: condition = is_float ? (fval1 == fval2) : (val1 == val2);
				break; // Beq
			case 0x2B:
			case 0x38: condition = is_float ? (fval1 >= fval2) : (val1 >= val2);
				break; // Bge
			case 0x2C:
			case 0x39: condition = is_float ? (fval1 > fval2) : (val1 > val2);
				break; // Bgt
			case 0x2D:
			case 0x3A: condition = is_float ? (fval1 <= fval2) : (val1 <= val2);
				break; // Ble
			case 0x2E:
			case 0x3B: condition = is_float ? (fval1 < fval2) : (val1 < val2);
				break; // Blt

			case 0x2F:
			case 0x3C: condition = is_float ? (fval1 != fval2) : ((uint32_t)val1 != (uint32_t)val2);
				break; // Bne_Un
			case 0x30:
			case 0x3D: condition = is_float ? (fval1 >= fval2) : ((uint32_t)val1 >= (uint32_t)val2);
				break; // Bge_Un
			case 0x31:
			case 0x3E: condition = is_float ? (fval1 > fval2) : ((uint32_t)val1 > (uint32_t)val2);
				break; // Bgt_Un
			case 0x32:
			case 0x3F: condition = is_float ? (fval1 <= fval2) : ((uint32_t)val1 <= (uint32_t)val2);
				break; // Ble_Un
			case 0x33:
			case 0x40: condition = is_float ? (fval1 < fval2) : ((uint32_t)val1 < (uint32_t)val2);
				break; // Blt_Un
			}

			if (condition)
			{
				ptr = my_stack->entry_il + offset;
				DBG
				("IL_Branch type 0x%02X, offset %d, condition true\n", ic, offset);
			}
			else
			{
				DBG
				("IL_Branch type 0x%02X, offset %d, condition false\n", ic, offset);
			}
			break;
		}

		case 0x41: // Ldind
		{
			uchar typeid = ReadByte;
			// POP
			POP;
			ASSERT_LANG(*eptr == Address, "IL_Ldind as typeid: %d, but stack is %d not address", typeid, *eptr);
			ASSERT_LANG(TypedAddrGetType(eptr) == typeid, "Ldind bad type, required %d, address refers to %d", typeid, TypedAddrGetType(eptr));

			uchar* valaddr = TypedAddrAsValPtr(eptr);
			*eptr = typeid;
			*(int*)(eptr + 1) = *(int*)valaddr; //whatever, just copy 32bit data.
			eptr += 8;

			DBG
			("IL_Ldind typeid: %d\n", typeid);

			break;
		}

		case 0x4C: // Stind
		{
			uchar typeid = ReadByte;
			// POP value
			POP;
			uchar* value = eptr;

			// POP address
			POP;

			ASSERT_LANG(*eptr == Address, "IL_Stind as typeid: %d, but stack is %d not address", typeid, *eptr);

			uchar* valaddr = TypedAddrAsValPtr(eptr);
			CPYVAL(valaddr, value + 1, typeid);

			DBG("IL_Stind typeid: %d\n", typeid);

			break;
		}

		case 0x4D:
		{
			int op = ReadByte;
			// POP second operand
			POP;
			uchar* ptr2 = eptr;
			uchar typeid2 = *eptr;
			// POP first operand
			POP;
			uchar* ptr1 = eptr;
			uchar typeid1 = *eptr;

			//todo: always perform 32bit operation.
			int val1, val2;
			float fval1, fval2;
			int ctype1 = 0, ctype2 = 0;
			switch (typeid1)
			{
			case 0:
			case 1: val1 = *(ptr1 + 1);
				break; // Byte
			case 2: val1 = *(char*)(ptr1 + 1);
				break;; // SByte
			case 3:
			case 4: val1 = *(short*)(ptr1 + 1);
				break;; // Int16
			case 5: val1 = *(unsigned short*)(ptr1 + 1);
				break;; // UInt16
			case 6:
			case 7: val1 = *(int*)(ptr1 + 1);
				break;; // (U)Int32
			case 8:
				fval1 = *(float*)(ptr1 + 1);
				ctype1 = 1;
				break; // single
			default: ASSERT_LANG(0, "unrecognized type");
			}

			switch (typeid2)
			{
			case 0:
			case 1: val2 = *(ptr2 + 1);
				break; // Byte
			case 2: val2 = *(char*)(ptr2 + 1);
				break;; // SByte
			case 3:
			case 4: val2 = *(short*)(ptr2 + 1);
				break;; // Int16
			case 5: val2 = *(unsigned short*)(ptr2 + 1);
				break;; // UInt16
			case 6:
			case 7: val2 = *(int*)(ptr2 + 1);
				break;; // (U)Int32
			case 8:
				fval2 = *(float*)(ptr2 + 1);
				ctype2 = 1;
				break; // single
			default: ASSERT_LANG(0, "unrecognized type");
			}

			// if (typeid1<8 )
			ASSERT_LANG(ctype1 == ctype2, "Type mismatch in arithmetic operation");
			if (ctype1 != ctype2) {
				break;
			}

			switch (ctype1)
			{
			case 0:
			{
				int a = val1;
				int b = val2;
				int result;
				switch (op)
				{
				case 0x60: result = a + b;
					break;
				case 0x61: result = a - b;
					break;
				case 0x62: result = a * b;
					break;
				case 0x63: result = a / b;
					break;
				case 0x64: result = (int)((unsigned int)a / (unsigned int)b);
					break;
				case 0x65: result = a % b;
					break;
				case 0x66: result = (int)((unsigned int)a % (unsigned int)b);
					break;
				case 0x67: result = a & b;
					break;
				case 0x68: result = a | b;
					break;
				case 0x69: result = a ^ b;
					break;
				case 0x6A: result = a << b;
					break;
				case 0x6B: result = a >> b;
					break;
				case 0x6C: result = (int)((unsigned int)a >> b);
					break;
				default: ASSERT_LANG(0, "Unsupported operation for Int32");
					return;
				}
				DBG
				("IL_Arithmetic int operation: %02X, %d=>%d=>%d\n", op, a, b, result);
				PUSH_STACK_INT(result);
				break;
			}
			case 1:
			{
				float a = fval1;
				float b = fval2;
				float result;
				switch (op)
				{
				case 0x60: result = a + b;
					break;
				case 0x61: result = a - b;
					break;
				case 0x62: result = a * b;
					break;
				case 0x63: result = a / b;
					break;
				default: ASSERT_LANG(0, "Unsupported operation for Single");
					return;
				}
				DBG
				("IL_Arithmetic float operation: %02X, %f=>%f=>%f\n", op, a, b, result);

				PUSH_STACK_FLOAT_D(result);
				break;
			}
			default:
				ASSERT_LANG(0, "Unsupported type for arithmetic operation typeid=%d", typeid1);
				break;
			}
			break;
		}

		case 0x6D:
		{ // Neg
			POP;
			uchar typeid = *eptr;

			switch (typeid)
			{
			case Int32:
			{
				PUSH_STACK_INT((-*(int*)(eptr + 1)));
				break;
			}
			case Single:
			{
				float tmp = -*(float*)(eptr + 1);
				PUSH_STACK_FLOAT_D(tmp);
				break;
			}
			default:
				ASSERT_LANG(0, "Unsupported type for neg operation typeid=%d", typeid);
				break;
			}
			DBG
			("IL_Neg operation\n");
			break;
		}

		case 0x6E: // Not
		{
			POP;
			uchar typeid = *eptr;
			ASSERT_LANG(typeid == Int32, "Unsupported type for not operation typeid=%d", typeid);

			PUSH_STACK_INT((~*(int*)(eptr + 1)));
			DBG("IL_Not operation\n");
			break;
		}

		case 0x70: // Conv_I1 (Convert to SByte)
		{
			POP
				char value = 0;
			switch (*eptr)
			{
			case Byte:
			case SByte: value = *(char*)(eptr + 1);
				break;
			case Int16:
			case UInt16: value = (char)*(short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = (char)*(int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to SByte");
			}
			PUSH_STACK_INT8(value);
			DBG
			("ConvI1: %02X\n", ic);
			break;
		}

		case 0x71: // Conv_U1 (Convert to Byte)
		{
			POP
				unsigned char value = 0;
			switch (*eptr)
			{
			case Byte:
			case SByte: value = *(eptr + 1);
				break;
			case Int16:
			case UInt16: value = (unsigned char)*(unsigned short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = (unsigned char)*(unsigned int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to Byte");
			}
			PUSH_STACK_UINT8(value);
			DBG
			("ConvU1: %02X\n", ic);
			break;
		}

		case 0x72: // Conv_I2 (Convert to Int16)
		{
			POP
				short value = 0;
			switch (*eptr)
			{
			case Byte: value = *(eptr + 1);
				break;
			case SByte: value = *(char*)(eptr + 1);
				break;
			case Int16:
			case UInt16: value = *(short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = (short)*(int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to Int16");
			}
			PUSH_STACK_INT16(value);
			DBG
			("ConvI2: %02X\n", ic);
			break;
		}

		case 0x73: // Conv_U2 (Convert to UInt16)
		{
			POP
				unsigned short value = 0;
			switch (*eptr)
			{
			case Byte: value = *(eptr + 1);
				break;
			case SByte: value = (unsigned short)*(char*)(eptr + 1);
				break;
			case Int16:
			case UInt16: value = *(unsigned short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = (unsigned short)*(unsigned int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to UInt16");
			}
			PUSH_STACK_UINT16(value);
			DBG
			("ConvU2: %02X\n", ic);
			break;
		}

		case 0x74: // Conv_I4 (Convert to Int32)
		{
			POP
				int value = 0;
			switch (*eptr)
			{
			case Byte: value = *(eptr + 1);
				break;
			case SByte: value = *(char*)(eptr + 1);
				break;
			case Int16: value = *(short*)(eptr + 1);
				break;
			case UInt16: value = *(unsigned short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = *(int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to Int32");
			}
			PUSH_STACK_INT(value);
			DBG
			("ConvI4: %02X\n", ic);
			break;
		}

		case 0x75: // Conv_U4 (Convert to UInt32)
		{
			POP
				unsigned int value = 0;
			switch (*eptr)
			{
			case Byte: value = *(eptr + 1);
				break;
			case SByte: value = (unsigned int)*(char*)(eptr + 1);
				break;
			case Int16: value = (unsigned int)*(short*)(eptr + 1);
				break;
			case UInt16: value = *(unsigned short*)(eptr + 1);
				break;
			case Int32:
			case UInt32: value = *(unsigned int*)(eptr + 1);
				break;
			case Single: int tvalue = *(int*)(eptr + 1);
				float f_value = *(float*)(&tvalue);
				value = f_value;
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to UInt32");
			}
			PUSH_STACK_UINT(value);
			DBG
			("ConvU4: %02X\n", ic);
			break;
		}

		case 0x76: // Conv_R4 (Convert to Single)
		{
			POP
				float value = 0;
			int tmp = *(int*)(eptr + 1); //let's just copy 4 bytes to tmp.
			switch (*eptr)
			{
			case Byte: value = (float)*(uchar*)(&tmp);
				break;
			case SByte: value = (float)*(char*)(&tmp);
				break;
			case Int16: value = (float)*(short*)(&tmp);
				break;
			case UInt16: value = (float)*(unsigned short*)(&tmp);
				break;
			case Int32: value = (float)tmp;
				break;
			case UInt32: value = (float)*(unsigned int*)(&tmp);
				break;
			case Single: value = *(float*)(&tmp);
				break;
			default: ASSERT_LANG(0, "Unsupported conversion to Single");
			}
			PUSH_STACK_FLOAT_D(value);
			DBG
			("ConvR4: %02X\n", ic);
			break;
		}

		case 0x77: // Conv_R_Un (Convert to Single, unsigned)
		{
			POP
				float value = 0;
			int tmp = *(int*)(eptr + 1);
			switch (*eptr)
			{
			case Byte: value = (float)*(uchar*)(&tmp);
				break;
			case UInt16: value = (float)*(unsigned short*)(tmp);
				break;
			case UInt32: value = (float)*(unsigned int*)(tmp);
				break;
			default: ASSERT_LANG(0, "Unsupported unsigned conversion to Single");
			}
			PUSH_STACK_FLOAT_D(value);
			DBG
			("ConvR_un: %02X\n", ic);
			break;
		}
		case 0x78: // initobj (struct zero-init)
		{
			// Not supported in this runtime path; just consume operand if any in future
			DBG("Initobj (noop)\n");
			break;
		}

		case 0x79: // Castclass (stack shape unchanged)
		{
			// For now, treat castclass as a no-op that leaves the reference on the stack.
			// The compiler validates statically when possible; runtime operand is not provided.
			DBG
			("Castclass (no-op)\n");
			break;
		}
		case 0x7A: // Newobj
		{
			int clsid = ReadShort;
			int op_type = ReadByte; // 0xA6: custom, 0xA7: builtin
			int method_id = ReadShort;

			// Create new object
			int id = newobj(clsid);

			char* mtype = "/";
			// Call constructor
			if (op_type == 0xA6)
			{
				// use new_obj_id.
				vm_push_stack(method_id, id, &eptr);
				mtype = "custom";
			}
			else if (op_type == 0xA7)
			{
				builtin_arg0 = id;
				// Builtin constructor
				if (method_id < NUM_BUILTIN_METHODS)
				{
					builtin_methods[method_id](&eptr);
				}
				else
				{
					ASSERT_LANG(0, "Error: Invalid builtin method ID: %d", method_id);
				}
				mtype = "builtin";
				builtin_arg0 = 0;
			}
			else
			{
				ASSERT_LANG(0, "Error: Unknown constructor type: %d", op_type);
			}

			// Push object reference onto stack
			PUSH_STACK_REFERENCEID(id);

			DBG
			("IL_Newobj, cls_%d, op[%s], m%d\n", clsid, mtype, method_id);
			break;
		}

		case 0x7B: // Ldfld or Ldsfld
		case 0x7C: // Ldflda or Ldsflda
		case 0x7D: // Stfld or Stsfld
		{
			uchar type = ReadByte;
			short offset = ReadShort;
			short aux = ReadShort;

			uchar is_static = type & 1;
			uchar is_cart_io = type & 2;

			if (is_cart_io)
			{
				// Handle CartIO operations
				int io_id = aux;
				uchar* field_ptr = statics_val_ptr + offset;

				POP; //cart io is located in ladderlogic::cart, which is an instanced field.
				uchar* val_ptr = eptr;
				if (ic == 0x7B)
				{
					// Ldfld (read from CartIO)
					PUSH_STACK_INDIRECT(field_ptr);
					DBG
					("Read CartIO: id=%d, offset=%d, type_%d\n", io_id, offset, *field_ptr);
				}
				else if (ic == 0x7C)
				{
					// Ldflda (get address of CartIO) 
					PUSH_STACK_ADDRESS(field_ptr + 1, *field_ptr);
					DBG
					("Get CartIO address: id=%d, offset=%d, type=%p\n", io_id, offset, *field_ptr);
				}
				else if (ic == 0x7D)
				{
					// Stfld (write to CartIO)
					POP;
					copy_val(field_ptr, val_ptr); //actually eptr
					SET_CART_IO_TOUCHED(io_id);
					DBG
					("Write CartIO: id=%d, typeid=%d\n", io_id, *field_ptr);
				}
			}
			else if (is_static)
			{
				// Handle static field operations
				uchar* field_ptr = statics_val_ptr + offset;
				if (ic == 0x7B)
				{
					// Ldsfld
					PUSH_STACK_INDIRECT(field_ptr);
					DBG
					("ldsfld type_%d from offset_%d\n", *field_ptr, offset);
				}
				else if (ic == 0x7C)
				{
					// Ldsflda
					PUSH_STACK_ADDRESS(field_ptr + 1, *field_ptr);
					DBG
					("ldsafld address, type_%d\n", *field_ptr);
				}
				else if (ic == 0x7D)
				{
					// Stsfld
					POP;
					copy_val(field_ptr, eptr);
					DBG
					("stflds type_%d to offset_%d\n", *field_ptr, offset);
				}
			}
			else
			{
				uchar* valptr = 0;
				if (ic == 0x7D)
				{
					POP;
					valptr = eptr;
				}

				POP;
				struct object_val* obj = 0;
				int ref_id = -1; // indicates it'a struct...
				if (*eptr == Address)
				{
					uchar* refval = TypedAddrAsValPtr(eptr);
					uchar atype = TypedAddrGetType(eptr);
					if (atype == ReferenceID)
					{
						ref_id = As(refval, int);
						DBG("ldfld from address found obj_%d ", ref_id);
						ASSERT_RT(ref_id != 0, "Null reference");
						obj = heap_obj[ref_id].pointer;
					}
					else if (atype == JumpAddress) // this is another jump.
					{
						obj = mem0 + As(refval, int);
					}
				}
				else if (*eptr == JumpAddress)
				{
					obj = TypedAddrAsValPtr(eptr);
				}
				else if (*eptr == ReferenceID)
				{
					ref_id = As(eptr + 1, int);
					ASSERT_RT(ref_id != 0, "Null reference");
					obj = heap_obj[ref_id].pointer;
				}
				else
					ASSERT_LANG(0, "IL_Field requires Reference ID");

                short actual_clsid = obj->clsid;
                short expected_clsid = aux;
                // Allow accessing base class fields on derived instances; just bounds-check against the actual class layout.
                if (actual_clsid < 0 || actual_clsid >= instanceable_class_N)
                {
                    ASSERT_LANG(0, "Object class id out of range (actual=%d, total=%d)", actual_clsid, instanceable_class_N);
                }
                if (offset >= instanceable_class_layout_ptr[actual_clsid].tot_size)
                {
                    // Heuristic: if expected class differs and the field fits expected class, redirect to method 'this'
                    if (expected_clsid >= 0 && expected_clsid < instanceable_class_N &&
                        offset < instanceable_class_layout_ptr[expected_clsid].tot_size &&
                        my_stack->args && my_stack->args[0] == ReferenceID)
                    {
                        int this_ref = As(my_stack->args + 1, int);
                        if (this_ref > 0)
                        {
                            struct object_val* this_obj = heap_obj[this_ref].pointer;
                            if (this_obj && this_obj->clsid == expected_clsid)
                            {
                                obj = this_obj;
                                actual_clsid = expected_clsid;
                            }
                        }
                    }
                    if (offset >= instanceable_class_layout_ptr[actual_clsid].tot_size)
                    {
                        ASSERT_LANG(0, "Field offset %d outside class %d size %d", offset, actual_clsid, instanceable_class_layout_ptr[actual_clsid].tot_size);
                    }
                }

				uchar* field_ptr = &obj->payload + offset;

				// Handle instance field operations
				if (ic == 0x7B || ic == 0x7C)
				{
					// Ldfld
					// POP object reference

					if (ic == 0x7B)
					{
						PUSH_STACK_INDIRECT(field_ptr);
						DBG("ldfld from %d(cls:%d, ofst:%d), type_%d\n", ref_id, aux, offset, *field_ptr);
					}
					else if (ic == 0x7C)
					{
						PUSH_STACK_ADDRESS(field_ptr + 1, *field_ptr);
						DBG("ldafld address from %d(cls:%d, ofst:%d), type_%d\n", ref_id, aux, offset, *field_ptr);
					}
				}
				else if (ic == 0x7D)
				{
					// Stfld
					copy_val(field_ptr, valptr); //actually eptr
				}
			}

			break;
		}

		case 0x8E: // Ldlen
		{
			POP;
			ASSERT_LANG(*eptr == ReferenceID, "Ldlen: Expected array reference");
			int arr_id = As(eptr + 1, int);
			ASSERT_RT(arr_id != 0, "Null reference");
			struct array_val* arr = heap_obj[arr_id].pointer;
			ASSERT_LANG(arr->header == ArrayHeader, "obj_%d is not an array", arr_id);
			PUSH_STACK_INT(arr->len);
			DBG
			("IL_Ldlen obj_%d => %d elements of %d\n", arr_id, arr->len, arr->typeid);
			break;
		}

		case 0x8F: // Ldelema
		{
			POP; // index
			int index = As(eptr + 1, int);
			POP; // array reference
			ASSERT_LANG(*eptr == ReferenceID, "Ldelema: Expected array reference");
			int arr_id = As(eptr + 1, int);
			ASSERT_RT(arr_id != 0, "Null reference");
			struct array_val* arr = heap_obj[arr_id].pointer;
			ASSERT_LANG(arr->header == ArrayHeader, "obj_%d is not an array", arr_id);
			ASSERT_RT(index >= 0 && index < arr->len, "Array index out of range: %d/%d", index, arr->len);
			int elem_size = get_type_sz(arr->typeid);
			uchar* elem_addr = &arr->payload + elem_size * index;

			uchar typeid = arr->typeid;
			if (arr->typeid == BoxedObject) // use in case like string.format.
			{
				typeid = elem_addr[0];
				elem_addr += 1;
			}
			PUSH_STACK_ADDRESS(elem_addr, typeid);
			DBG
			("IL_Ldelema obj_%d %d-th elem\n", arr_id, index);
			break;
		}

		case 0x90: // Ldelem
		{
			uchar typeid = ReadByte;

			POP; // index
			int index = As(eptr + 1, int);
			POP; // array reference
			ASSERT_LANG(*eptr == ReferenceID, "Ldelem: Expected array reference");
			int arr_id = As(eptr + 1, int);
			ASSERT_RT(arr_id != 0, "Null reference");
			struct array_val* arr = heap_obj[arr_id].pointer;
			ASSERT_LANG(arr->header == ArrayHeader, "obj_%d is not an array", arr_id);
			ASSERT_RT(index >= 0 && index < arr->len, "Array index out of range: %d/%d", index, arr->len);
			ASSERT_LANG(arr->typeid == typeid, "Ldelem: Type mismatch");
			int elem_size = get_type_sz(typeid);
			uchar* elem_addr = &arr->payload + (elem_size)*index;

			*eptr = arr->typeid;
			*(int*)(eptr + 1) = *(int*)elem_addr; //just the fuck copy 32bit
			eptr += 8;
			//PUSH_STACK_INDIRECT(elem_addr);

			DBG("IL_Ldelem type_%d from obj_%d[%d]\n", typeid, arr_id, index);
			break;
		}

		case 0x91: // Stelem
		{
			uchar typeid = ReadByte; //todo: this could be just elem_sz.

			POP; // value to store
			uchar* value = eptr;
			POP; // index
			int index = As(eptr + 1, int);
			POP; // array reference
			ASSERT_LANG(*eptr == ReferenceID, "Stelem: Expected array reference");
			int arr_id = As(eptr + 1, int);
			ASSERT_RT(arr_id != 0, "Null reference");
			struct array_val* arr = heap_obj[arr_id].pointer;
			ASSERT_LANG(arr->header == ArrayHeader, "obj_%d is not an array", arr_id);
			ASSERT_RT(index >= 0 && index < arr->len, "Array index out of range: %d/%d", index, arr->len);

			int elem_size = get_type_sz(arr->typeid);
			uchar* elem_addr = &arr->payload + elem_size * index;

			if (arr->typeid == BoxedObject) {
				elem_addr[0] = value[0];
				copy_val(elem_addr, value);
			}
			else
			{
				if(arr->typeid != typeid) 
					DBG("array_%d is type %d but stelem as %d\n", arr_id, arr->typeid, typeid);
				CPYVAL(elem_addr, value+1, arr->typeid)
			}

			DBG
			("IL_Stelem typeid_%d to obj_%d[%d]\n", typeid, arr_id, index);
			break;
		} 


		case 0xA0: // Callvirt (abstract)
		{
			DBG
			("IL_Callvirt polymorphism \n");
			short vmethod_id = ReadShort;



			// Find the actual method ID for this class and virtual method
			int actual_method_id;
			uchar* ptr = virt_table + *((short*)(virt_ptr + 2) + vmethod_id);
			uchar ncls = *ptr;
			uchar paramCnt = *(ptr + 1);
			DBG("callvirt abstract: ncls=%d paramCnt=%d stackDepth=%d\n", ncls, paramCnt, my_stack->stack_depth);
			uchar* o_eptr = eptr;
			for (int i = 0; i < paramCnt; ++i)
				POP;

			POP;
			ASSERT_LANG(*eptr == ReferenceID, "this pointer should be reference id");
			int instance_ref = As(eptr + 1, int);

			ASSERT_RT(instance_ref != 0, "Null reference");
			// Get the object from the heap
			struct object_val* obj = (struct object_val*)heap_obj[instance_ref].pointer;
			ASSERT_LANG(obj->header == ObjectHeader, "this is not an object header");
			struct
			{
				short clsid;
				short methodid;
			}*vm_s = ptr + 2;
			for (int i = 0; i < ncls; ++i, vm_s++)
				if (vm_s->clsid == obj->clsid)
				{
					actual_method_id = vm_s->methodid;
					goto actual_method_id_initialization_finish;
				}

			ASSERT_LANG(0, "Cannot find vmethod %d for type %d", vmethod_id, obj->clsid);
		actual_method_id_initialization_finish:

			eptr = o_eptr;
			// Call the method (similar to regular call)
			vm_push_stack(actual_method_id, -1, &eptr);
			break;
		}

		case 0xA1: // Ldftn or Ldtoken
		{
			uchar address_type = ReadByte;
			if (address_type != Address)
			{
				DBG
				("Error: Expected Address type for Ldftn/Ldtoken\n");
				break;
			}

			uchar subcode = ReadByte;
			switch (subcode)
			{
			case 0xA6: // Code.ldftn (custom method)
			case 0xA7: // Code.ldftn (builtin method)
			{
				unsigned short method_id = ReadShort;
				struct method_pointer tmp = { .type = subcode == 0xA6 ? 1 : 0, .id = method_id };
				PUSH_STACK_METHODHANDLER(tmp);
				DBG
				("IL_Ldftn %s method, method_id: %d\n",
					(subcode == 0xA6) ? "custom" : "builtin", method_id);
				break;
			}
			case 0x11: // Code.ldtoken, only used by array initializer(len is given by array object).
			{
				unsigned short data_len = ReadShort;
				uchar* data_address = ptr;
				ptr += data_len; // Skip the data
				PUSH_STACK_ADDRESS(data_address, Metadata);
				DBG
				("IL_Ldtoken data length: %d, address: %p\n", data_len, data_address);
				break;
			}
			default:
				DBG
				("Unknown subcode for Ldftn/Ldtoken: %02X\n", subcode);
				break;
			}
			break;
		}

		case 0xA2: // Callvirt (instanced)
		{
			DBG
			("IL_Callvirt instanced\n");
			if (eptr <= my_stack->evaluation_st_ptr)
			{
				DBG("callvirt: stack empty before dispatch (method=%d depth=%d)\n", my_stack->method_id, my_stack->stack_depth);
			}
			else
			{
				DBG("callvirt: stack top type=%d ref=%d\n", (int)*(eptr - STACK_STRIDE), As(eptr - STACK_STRIDE + 1, int));
			}
			ic = ReadByte;
			switch (ic)
			{
			case 0xA6: // Call (custom method)  
			{
				short method_id = ReadShort;
				DBG
				("to call custom %d\n", method_id);
				vm_push_stack(method_id, -1, &eptr);
				break;
			}

			case 0xA7: // Call (built-in method)
			{
				short method_id = ReadShort;
				if (method_id < NUM_BUILTIN_METHODS)
				{
					builtin_methods[method_id](&eptr);
					DBG
					("call builtin method %d, ret type_%d\n", method_id, *(eptr - STACK_STRIDE));
				}
				else
				{
					ASSERT_LANG(0, "Invalid built-in method ID: %d", method_id);
				}
				break;
			}
			}
			break;
		}

		case 0xA6: // Call (custom method)  
		{
			short method_id = ReadShort;
			DBG
			("to call custom %d\n", method_id);
			vm_push_stack(method_id, -1, &eptr);
			break;
		}

		case 0xA7: // Call (built-in method)
		{
			short method_id = ReadShort;
			if (method_id < NUM_BUILTIN_METHODS)
			{
				DBG("calling builtin method %d...", method_id);
				builtin_methods[method_id](&eptr);
				DBG("  ret type_%d\n", *(eptr - STACK_STRIDE));
			}
			else
			{
				ASSERT_LANG(0, "Invalid built-in method ID: %d", method_id);
			}
			break;
		}

		case 0xA8: // Calli
		{
			// Implement indirect method call logic here
			DBG
			("IL_Calli ??\n");
			break;
		}

		case 0xE2: // Ceq
		case 0xE3: // Cgt
		case 0xE4: // Cgt_Un
		case 0xE5: // Clt
		case 0xE6: // Clt_Un
		{
			// if (il_cnt > 622)
			// 	printf("CHK");
			DBG
			("IL_Comparison operation: %02X\n", ic);

			// Pop the second operand
			POP;
			uchar type2 = *eptr;
			uchar* value2 = eptr + 1;

			// Pop the first operand
			POP;
			uchar type1 = *eptr;
			uchar* value1 = eptr + 1;

			int v1, v2;
			float fv1, fv2;
			int useF = 0;
			switch (type1)
			{
			case Boolean: case SByte: v1 = As(value1, char); break;
			case Byte: v1 = As(value1, unsigned char); break;
			case Int16: v1 = As(value1, short); break;
			case UInt16: v1 = As(value1, unsigned short); break;
			case Int32: case ReferenceID: v1 = As(value1, int); break;
			case UInt32: v1 = As(value1, unsigned int); break;
			case Single: fv1 = As(value1, float); useF = 1; break;
			}
			switch (type2)
			{
			case Boolean: case SByte: v2 = As(value2, char); break;
			case Byte: v2 = As(value2, unsigned char); break;
			case Int16: v2 = As(value2, short); break;
			case UInt16: v2 = As(value2, unsigned short); break;
			case Int32:  case ReferenceID: v2 = As(value2, int); break;
			case UInt32: v2 = As(value2, unsigned int); break;
			case Single: fv2 = As(value2, float); useF = 1; break;
			}

			int result = 0;
			if (!useF) {
				switch (ic)
				{
				case 0xE2: result = (v1 == v2);
					break;
				case 0xE3: result = (v1 > v2);
					break;
				case 0xE4: result = ((unsigned int)v1 > (unsigned int)v2);
					break;
				case 0xE5: result = (v1 < v2);
					break;
				case 0xE6: result = ((unsigned int)v1 < (unsigned int)v2);
					break;
				}
			}
			else
			{
				switch (ic)
				{
				case 0xE2: result = (fv1 == fv2);
					break;
				case 0xE3: result = (fv1 > fv2);
					break;
				case 0xE5: result = (fv1 < fv2);
					break;
				default:
					ASSERT_LANG(0, "bad comparison op_%d for single", ic);
				}
			}

			// Push the result onto the stack
			PUSH_STACK_INT(result);
			break;
		}

		case 0x50:
		{
			unsigned short n = ReadShort;

			// POP
			POP;
			uchar typeid = *eptr;

			ASSERT_LANG(typeid == 6, "IL_Switch requires int, actual=%d", typeid);

			unsigned int jmp = As(eptr + 1, int);
			if (jmp < n)
			{
				unsigned short* sw_ptr = ptr;
				ptr = my_stack->entry_il + sw_ptr[jmp];
				DBG
				("IL_Switch, %d cases, hit case_%d -> offset_%d\n", n, jmp, sw_ptr[jmp]);
			}
			else
			{
				ptr += 2 * n;
				DBG
				("IL_Switch of %d cases, fall through.", n);
			}
			break;
		}
		default:
			ASSERT_LANG(0, "Unknown instruction: 0x%02X", ic);
		}

		ASSERT_LANG(ptr < virt_ptr, "bad program counter");
		my_stack->PC = ptr; // Update program counter
		my_stack->evaluation_pointer = eptr;

	}

exited:
	new_stack_depth--;
	DBG("<<< custom method %d finish\n", method_id);
}

void reset_cart_IO_stored() {
	memset(cart_IO_stored, 0, sizeof(cart_IO_stored));
}

// Helper function to mark and traverse objects
void mark_object(int obj_id)
{
	ASSERT_LANG(obj_id >= 0 && obj_id < heap_newobj_id, "invalid reference id %d", obj_id);
	if (obj_id == 0 || heap_obj[obj_id].new_id != -1)
		return;

	heap_obj[obj_id].new_id = -2; // Mark as visited

	uchar* header = heap_obj[obj_id].pointer;
	DBG("Marked obj_%d, header_%d\n", obj_id, *header);

	if (*header == ArrayHeader)
	{
		struct array_val* arr = header;
		if (arr->typeid == ReferenceID)
		{
			for (int i = 0; i < arr->len; ++i)
			{
				int* ref_id_ptr = &arr->payload + get_type_sz(ReferenceID) * i;
				if (*ref_id_ptr != 0)
					mark_object(*ref_id_ptr);
			}
		}
	}
	else if (*header == ObjectHeader)
	{
		struct object_val* obj = header;
		short clsid = obj->clsid;
		if (clsid & 0xf000)
		{
			// buitin classes.
			int b_clsid = (short)(clsid - 0xf000);
			uchar* ftype = builtin_cls[b_clsid];
			uchar* ptr = &obj->payload;
			for (int j = 0; j < *ftype; ++j)
			{
				int typeid = ftype[j + 1];
				ASSERT_LANG(typeid == *ptr, "bad builtin_cls %d on obj_%d", b_clsid, obj_id);
				if (typeid == ReferenceID)
				{
					int* ref_id_ptr = ptr + 1;
					if (*ref_id_ptr != 0)
						mark_object(*ref_id_ptr);
				}
				ptr += get_val_sz(typeid);
			}
		}
		else {
			struct per_field* layout = instanceable_class_per_layout_ptr + instanceable_class_layout_ptr[clsid].layout_offset;
			int field_count = instanceable_class_layout_ptr[clsid].n_of_fields;

			for (int j = 0; j < field_count; j++)
			{
				if (layout[j].typeid == ReferenceID)
				{
					int* ref_id_ptr = &obj->payload + layout[j].offset + 1;
					if (*ref_id_ptr != 0)
						mark_object(*ref_id_ptr);
				}
			}
		}
	}
	else if (*header == StringHeader)
	{
		// do nothing.
	}
}

void clean_up()
{
	DBG("Starting heap cleanup\n");

	int prev_obj_n = heap_newobj_id;
	// Reset all new_id to -1
	for (int i = 1; i < heap_newobj_id; i++)
		heap_obj[i].new_id = -1;


	// Start traversal from LadderLogic root object
	DBG("mark root: ");
	if (ladderlogic_this_refid > 0)
		mark_object(ladderlogic_this_refid);

	// also need to traverse all statics
	{
		uchar* ptr_s = statics_val_ptr;
		for (int i = 0; i < statics_amount; ++i)
		{
			DBG("mark static %d: ", i);
			uchar typeid = *ptr_s;
			if (typeid == ReferenceID)
				mark_object(As(ptr_s + 1, int));
			ptr_s += get_val_sz(typeid);
		}
	}

	// Assign new IDs to marked objects
	int new_id = 1;
	for (int i = 1; i < heap_newobj_id; i++)
	{
		if (heap_obj[i].new_id == -2)
		{
			heap_obj[i].new_id = new_id++;
			DBG("Assigned obj_%d newid: %d\n", i, heap_obj[i].new_id);
		}
	}

	// update referenceid for static objs:
	{
		uchar* ptr_s = statics_val_ptr;
		for (int i = 0; i < statics_amount; ++i)
		{
			uchar typeid = *ptr_s;
			if (typeid == ReferenceID) {
				int* ref = ptr_s + 1;
				ASSERT_LANG(*ref < heap_newobj_id, "Invalid reference %d >= %d", *ref, heap_newobj_id);
				if (*ref > 0)
					*ref = heap_obj[*ref].new_id;
			}
			ptr_s += get_val_sz(typeid);
		}
	}

	// Update reference IDs in heap objects
	for (int i = 1; i < heap_newobj_id; i++)
	{
		if (heap_obj[i].new_id != -1)
		{
			uchar* header = heap_obj[i].pointer;
			if (*header == ArrayHeader)
			{
				struct array_val* arr = header;
				if (arr->typeid == ReferenceID)
				{
					for (int j = 0; j < arr->len; ++j)
					{
						int* ref_id_ptr = &arr->payload + get_type_sz(ReferenceID) * j;
						int old_id = *ref_id_ptr;
						if (old_id > 0 && old_id < heap_newobj_id)
						{
							*ref_id_ptr = heap_obj[old_id].new_id;
							DBG("Updated rid in old custom obj_%d[%d]: refid %d to %d\n", i, j, old_id, *ref_id_ptr);
						}
					}
				}
			}
			else if (*header == ObjectHeader)
			{
				struct object_val* obj = header;
				short clsid = obj->clsid;
				if (clsid & 0xf000)
				{
					// buitin classes.
					short b_clsid = (short)(clsid - 0xf000);
					uchar* ftype = builtin_cls[b_clsid];
					uchar* ptr = &obj->payload;
					for (int j = 0; j < *ftype; ++j)
					{
						int typeid = ftype[j + 1];
						if (typeid == ReferenceID)
						{
							int* ref_id_ptr = ptr + 1;
							int old_id = *ref_id_ptr;
							if (old_id > 0 && old_id < heap_newobj_id)
							{
								*ref_id_ptr = heap_obj[old_id].new_id;
								DBG("Updated rid in old builtin obj_%d(bcls_%d), field %d: refid %d to %d\n", i, b_clsid, j, old_id, *ref_id_ptr);
							}
						}
						ptr += get_val_sz(typeid);
					}
				}
				else {
					struct per_field* layout = instanceable_class_per_layout_ptr + instanceable_class_layout_ptr[clsid].layout_offset;
					int field_count = instanceable_class_layout_ptr[clsid].n_of_fields;

					for (int j = 0; j < field_count; j++)
					{
						if (layout[j].typeid == ReferenceID)
						{
							int* ref_id_ptr = (int*)(&obj->payload + layout[j].offset + 1);
							int old_id = *ref_id_ptr;
							if (old_id > 0 && old_id < heap_newobj_id)
							{
								*ref_id_ptr = heap_obj[old_id].new_id;
								DBG("Updated rid in old custom obj_%d(cls_%d), field %d: refid %d to %d\n", i, clsid, j, old_id, *ref_id_ptr);
							}
						}
					}
				}
			}
			else if (*header == StringHeader)
			{
				// do nothing.
			}


		}
	}

	for (int i = 1; i < heap_newobj_id; ++i)
	{
		uchar* header = heap_obj[i].pointer;
		ASSERT_LANG(*header == ArrayHeader || *header == ObjectHeader || *header == StringHeader, "bad heap header! header=%d", *header);
	}

	// Compact heap
	uchar* tail = heap_obj[1].pointer;
	int lastobj = 1;
	for (int i = 2; i < heap_newobj_id; i++)
	{
		int nid = heap_obj[i].new_id;
		if (nid != -1) {
			if (i != nid)
			{
				uchar* originalPtr = heap_obj[i].pointer;
				uchar* lastPtr = heap_obj[i - 1].pointer;
				// copy from lastPtr to originalPtr.
				int len = lastPtr - originalPtr;
				uchar* newPtr = tail - len;
				for (int p = len - 1; p >= 0; --p)
					newPtr[p] = originalPtr[p];
				tail = heap_obj[nid].pointer = newPtr;
				lastobj = nid;
				DBG("Moved object from index %d to %d, len=%d\n", i, nid, len);
			}
			else {
				// keep position.
				tail = heap_obj[nid].pointer;
				lastobj = nid;
			}
		}
	}
	heap_newobj_id = lastobj + 1;
	DBG("Heap cleanup complete. objcnt: %d->%d, size=%dB\n", prev_obj_n, lastobj, heap_tail - tail);


	for (int i = 1; i < heap_newobj_id; ++i)
	{
		uchar* header = heap_obj[i].pointer;
		ASSERT_LANG(*header == ArrayHeader || *header == ObjectHeader || *header == StringHeader, "bad heap header! header=%d", *header);
	}
}


void vm_sort_slots();

void vm_run(int iteration)
{
	ASSERT_LANG(snapshot_state != 0, "Must update machine snapshot state before new iteration");

	// swap buffer.
	enter_critical();
	uchar* tmp = processing_buf;
	processing_buf = writing_buf;
	writing_buf = tmp;
	writing_buf->offset = 0;
	writing_buf->N_slots = 0;
	leave_critical();
	vm_sort_slots();

	// clear all cart_IO touched.
	reset_cart_IO_stored();

	// put refreshed cart_IO static vals.

	// start running.
	iterations = iteration;
	vm_push_stack(entry_method_id, -1, 0);

	// clean up.
	clean_up();
	snapshot_state = 0;
}

// New layout for upper input buffer:
// [ArrayHeader=11][Byte=1][len:4]{ per-field payload ... }
// per-field payload item is one of:
//   - [typeid (primitive 0..8 or 3=Char)] [value bytes]
//   - [StringHeader=12][len:2][bytes]
//   - [ArrayHeader=11][elemTid:1][len:4][raw bytes]
//   - [ReferenceID=16][rid:4] (null if rid==0)

void vm_put_upper_memory(uchar* buffer, int size)
{
	uchar* ptr = buffer;
	uchar* end = buffer + size;

	for (int cid = 0; cid < cartIO_N; ++cid)
	{
		ASSERT_RT(ptr < end, "upper buffer truncated at field %d", cid);
		uchar* field_ptr = statics_val_ptr + cartIO_layout_ptr[cid];
		uchar expected_tid = *field_ptr;
		uchar token = *ptr; ptr += 1;

		if (expected_tid == ReferenceID)
		{
			if (token == ReferenceID)
			{
				int rid = As(ptr, int); ptr += 4;
				*field_ptr = ReferenceID;
				As(field_ptr + 1, int) = rid;
			}
			else if (token == StringHeader)
			{
				unsigned short slen = As(ptr, unsigned short); ptr += 2;
				int rid = newstr((short)slen, ptr);
				ptr += slen;
				*field_ptr = ReferenceID;
				As(field_ptr + 1, int) = rid;
			}
			else if (token == ArrayHeader)
			{
				uchar elem_tid = *ptr; ptr += 1;
				int arr_len = As(ptr, int); ptr += 4;
				if (!(elem_tid == Boolean || elem_tid == Byte || elem_tid == SByte || elem_tid == 3 ||
					elem_tid == Int16 || elem_tid == UInt16 || elem_tid == Int32 || elem_tid == UInt32 || elem_tid == Single))
					ASSERT_LANG(0, "upper put: array element type %d not allowed", elem_tid);
				int elem_sz = get_type_sz(elem_tid);
				ASSERT_RT(ptr + elem_sz * arr_len <= end, "upper buffer array payload overflow");
				int rid = newarr((short)arr_len, elem_tid);
				struct array_val* arr = heap_obj[rid].pointer;
				memcpy(&arr->payload, ptr, elem_sz * arr_len);
				ptr += elem_sz * arr_len;
				*field_ptr = ReferenceID;
				As(field_ptr + 1, int) = rid;
			}
			else
			{
				ASSERT_LANG(0, "upper put: expected ReferenceID payload (string/array/ref), got token %d", token);
			}
		}
		else
		{
			ASSERT_LANG(token == expected_tid, "put cart_io:%d expected type %d, recv:%d", cid, expected_tid, token);
			int sz = get_type_sz(expected_tid);
			ASSERT_RT(ptr + sz <= end, "upper buffer primitive overflow");
			memcpy(field_ptr + 1, ptr, sz);
			ptr += sz;
		}
	}

	ASSERT_RT(ptr == end, "upper buffer size mismatch: leftover %d bytes", (int)(end - ptr));
}

int lowerUploadSz;
uchar* vm_get_lower_memory()
{
	ASSERT_LANG(new_stack_depth == 0, "Must perform get_lower_memory after VM execution");
	uchar* lowerUpload = stack0;
	uchar* lptr = lowerUpload;

	// first 4 bytes: iterations
	As(lptr, int) = iterations; lptr += 4;

	for (int i = 0; i < cartIO_N; ++i)
	{
		uchar* field_ptr = statics_val_ptr + cartIO_layout_ptr[i];
		uchar type_id = *field_ptr;

		if (type_id == ReferenceID)
		{
			int rid = As(field_ptr + 1, int);
			if (rid == 0)
			{
				*lptr = ReferenceID; lptr += 1;
				As(lptr, int) = 0; lptr += 4;
				continue;
			}
			uchar* header = heap_obj[rid].pointer;
			if (*header == StringHeader)
			{
				struct string_val* str = (struct string_val*)header;
				*lptr = StringHeader; lptr += 1;
				As(lptr, unsigned short) = str->str_len; lptr += 2;
				memcpy(lptr, &str->payload, str->str_len);
				lptr += str->str_len;
			}
			else if (*header == ArrayHeader)
			{
				struct array_val* arr = (struct array_val*)header;
				uchar elem_tid = arr->typeid;
				if (!(elem_tid == Boolean || elem_tid == Byte || elem_tid == SByte || elem_tid == 3 ||
					elem_tid == Int16 || elem_tid == UInt16 || elem_tid == Int32 || elem_tid == UInt32 || elem_tid == Single))
					ASSERT_LANG(0, "lower get: array element type %d not allowed", elem_tid);
				*lptr = ArrayHeader; lptr += 1;
				*lptr = elem_tid; lptr += 1;
				As(lptr, int) = arr->len; lptr += 4;
				int elem_sz = get_type_sz(elem_tid);
				memcpy(lptr, &arr->payload, elem_sz * arr->len);
				lptr += elem_sz * arr->len;
			}
			else
			{
				ASSERT_LANG(0, "lower get: ReferenceID points to unsupported header %d", *header);
			}
		}
		else
		{
			*lptr = type_id; lptr += 1;
			int sz = get_type_sz(type_id);
			memcpy(lptr, field_ptr + 1, sz);
			lptr += sz;
		}
	}

	lowerUploadSz = (int)(lptr - lowerUpload);
	return lowerUpload;
}

int vm_get_lower_memory_size()
{
	return lowerUploadSz;
}

void vm_put_buffer(uchar* buffer, int size, uchar type, int aux0, int aux1)
{
	enter_critical();
	int myslot = writing_buf->N_slots;
	ASSERT_RT(myslot < SLOT_NUMBER, "device IO buffer slots overflown");
	writing_buf->N_slots += 1;
	int myoffset = writing_buf->offset;
	ASSERT_RT(writing_buf->offset + size <= BUF_SZ, "device IO buffer size overflown");
	writing_buf->offset += size;
	leave_critical();

	writing_buf->slots[myslot].type = type;
	writing_buf->slots[myslot].offset = myoffset;
	writing_buf->slots[myslot].aux0 = aux0;
	writing_buf->slots[myslot].aux1 = aux1;
	writing_buf->slots[myslot].len = size;
	memcpy(&writing_buf->payload + myoffset, buffer, size);
}

#define SNAPSHOT_TYPE 0x55
#define STREAM_TYPE 0x99
#define EVENT_TYPE 0xbb

void vm_put_snapshot_buffer(uchar* buffer, int size)
{
	vm_put_buffer(buffer, size, SNAPSHOT_TYPE, 0, 0);
	snapshot_state = 1;
}

void vm_put_stream_buffer(int streamID, uchar* buffer, int size)
{
	vm_put_buffer(buffer, size, STREAM_TYPE, streamID, 0);
}

void vm_put_event_buffer(int portID, int eventID, uchar* buffer, int size)
{
	vm_put_buffer(buffer, size, EVENT_TYPE, portID, eventID);
}

void vm_quick_sort_slots(int left, int right) {
	int i = left, j = right;
	unsigned int pivot = processing_buf->slots[sorted_slots[(left + right) / 2]].sortable;

	// Partition
	while (i <= j) {
		while (processing_buf->slots[sorted_slots[i]].sortable < pivot)
			i++;
		while (processing_buf->slots[sorted_slots[j]].sortable > pivot)
			j--;
		if (i <= j) {
			// Swap
			short temp = sorted_slots[i];
			sorted_slots[i] = sorted_slots[j];
			sorted_slots[j] = temp;
			i++;
			j--;
		}
	}

	// Recursion
	if (left < j)
		vm_quick_sort_slots(left, j);
	if (i < right)
		vm_quick_sort_slots(i, right);
}

void vm_sort_slots() {
	// Initialize sorted_slots with indices
	for (int i = 0; i < SLOT_NUMBER; i++)
		sorted_slots[i] = i;

	// Perform quick sort
	vm_quick_sort_slots(0, processing_buf->N_slots - 1);
}



/// ##########################################################################################
///
///    ┳┓  •┓ •    ┏      •      •     ┓             •     
///    ┣┫┓┏┓┃╋┓┏┓  ╋┓┏┏┓┏╋┓┏┓┏┓  ┓┏┳┓┏┓┃┏┓┏┳┓┏┓┏┓╋┏┓╋┓┏┓┏┓┏
///    ┻┛┗┻┗┗┗┗┛┗  ┛┗┻┛┗┗┗┗┗┛┛┗  ┗┛┗┗┣┛┗┗ ┛┗┗┗ ┛┗┗┗┻┗┗┗┛┛┗┛
///                     
/// ##########################################################################################
///
///	All builtin related implementations start from here.


#define BUILTIN_CLSID_BASE 0xF000
#define BUILTIN_CLSID(idx) (BUILTIN_CLSID_BASE + (idx))


// PUSH - optimized to avoid unnecessary memset
#define PUSH_STACK_INT8(val) **reptr = SByte; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT8(val) **reptr = Byte; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_INT16(val) **reptr = Int16; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT16(val) **reptr = UInt16; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_INT(val) **reptr = Int32; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT(val) **reptr = UInt32; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_FLOAT_D(val) **reptr = Single; As(*reptr + 1, float) = val; ((int*)*reptr)[1] = 0; *reptr+=8;

#define PUSH_STACK_FLOAT_M(val) { **reptr = Single; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8; }

// not on reference id: it's heap object ID, not address!!!
#define PUSH_STACK_REFERENCEID(val) **reptr = ReferenceID; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;

#undef PUSH_STACK_INDIRECT
#define POP {(*reptr)-=8;}

// Helper functions 
#define bool uchar
#define true 1
#define false 0


static uchar* pop_value_type_slot(uchar** reptr, const char* where);

#define STACK_VALUE_SIZE STACK_STRIDE
typedef struct { uchar bytes[STACK_VALUE_SIZE]; } stack_value_t;

INLINE void stack_value_copy(stack_value_t* dst, const uchar* src) { 
	memcpy(dst->bytes, src, STACK_VALUE_SIZE); 
}
INLINE void stack_value_store(uchar* dst, const stack_value_t* value) { memcpy(dst, value->bytes, STACK_VALUE_SIZE); }
INLINE uchar stack_value_type(const stack_value_t* value) { return value->bytes[0]; }
INLINE void push_stack_value(uchar** reptr, const stack_value_t* value) { memcpy(*reptr, value->bytes, STACK_VALUE_SIZE); *reptr += STACK_STRIDE; }
INLINE int stack_value_as_int(const stack_value_t* value) { return *(int*)(value->bytes + 1); }
INLINE float stack_value_as_float(const stack_value_t* value) { return *(float*)(value->bytes + 1); }

// todo: time consuming, just write the index in source code!
INLINE int builtin_field_offset_by_index(int clsidx, int field_idx)
{
	uchar* layout = builtin_cls[clsidx];
	int offset = 0;
	for (int i = 0; i < field_idx; ++i)
		offset += get_val_sz(layout[i + 1]);
	return offset;
}

INLINE uchar* builtin_field_ptr_by_index(struct object_val* obj, int clsidx, int field_idx)
{
	return &obj->payload + builtin_field_offset_by_index(clsidx, field_idx);
}

INLINE int* builtin_field_int_ptr(struct object_val* obj, int clsidx, int field_idx)
{
	uchar* field = builtin_field_ptr_by_index(obj, clsidx, field_idx);
	ASSERT_LANG(field[0] == Int32, "Field %d of clsidx %d is not Int32 (type=%d)", field_idx, clsidx, field[0]);
	return (int*)(field + 1);
}

INLINE int builtin_field_get_reference(struct object_val* obj, int clsidx, int field_idx)
{
	uchar* field = builtin_field_ptr_by_index(obj, clsidx, field_idx);
	ASSERT_LANG(field[0] == ReferenceID, "Field %d of clsidx %d is not ReferenceID (type=%d)", field_idx, clsidx, field[0]);
	return *(int*)(field + 1);
}

INLINE void builtin_field_set_reference(struct object_val* obj, int clsidx, int field_idx, int ref_id)
{
	uchar* field = builtin_field_ptr_by_index(obj, clsidx, field_idx);
	ASSERT_LANG(field[0] == ReferenceID, "Field %d of clsidx %d is not ReferenceID (type=%d)", field_idx, clsidx, field[0]);
	*(int*)(field + 1) = ref_id;
}

INLINE int builtin_field_get_int(struct object_val* obj, int clsidx, int field_idx)
{
	return *builtin_field_int_ptr(obj, clsidx, field_idx);
}

INLINE void builtin_field_set_int(struct object_val* obj, int clsidx, int field_idx, int value)
{
	*builtin_field_int_ptr(obj, clsidx, field_idx) = value;
}

INLINE struct object_val* expect_builtin_obj(int ref_id, int clsidx, const char* where)
{
	ASSERT_LANG(ref_id > 0 && ref_id < heap_newobj_id, "%s: invalid reference id %d", where, ref_id);
	struct object_val* obj = (struct object_val*)heap_obj[ref_id].pointer;
	if (obj == NULL || obj->header != ObjectHeader)
		ASSERT_LANG(0, "%s: reference %d does not point to an object (header=%d)", where, ref_id, obj ? obj->header : -1);
	if (obj->clsid != BUILTIN_CLSID(clsidx))
		ASSERT_LANG(0, "%s: builtin object expected clsid %d but got %d", where, BUILTIN_CLSID(clsidx), obj->clsid);
	return obj;
}

// Fast unchecked cast for performance-critical paths where type safety is guaranteed by compiler
INLINE struct object_val* cast_builtin_obj(int ref_id)
{
	return (struct object_val*)heap_obj[ref_id].pointer;
}

INLINE struct array_val* cast_array(int ref_id)
{
	return (struct array_val*)heap_obj[ref_id].pointer;
}

INLINE struct array_val* expect_array(int ref_id, uchar expected_type, const char* where)
{
	ASSERT_LANG(ref_id > 0 && ref_id < heap_newobj_id, "%s: invalid array reference id %d", where, ref_id);
	uchar* header = heap_obj[ref_id].pointer;
	if (header == NULL || *header != ArrayHeader)
		ASSERT_LANG(0, "%s: reference %d does not point to an array (header=%d)", where, ref_id, header ? *header : -1);
	struct array_val* arr = (struct array_val*)header;
	ASSERT_LANG(expected_type == 0xFF || arr->typeid == expected_type, "%s: expected array type %d but got %d", where, expected_type, arr->typeid);
	return arr;
}

INLINE void stack_value_clear(stack_value_t* value)
{
	memset(value->bytes, 0, STACK_VALUE_SIZE);
}

INLINE void stack_value_from_array_elem(stack_value_t* dst, struct array_val* arr, int index)
{
	uchar typeid = arr->typeid;
	int elem_sz = get_type_sz(typeid);
	stack_value_clear(dst);
	dst->bytes[0] = typeid;
	memcpy(dst->bytes + 1, &arr->payload + index * elem_sz, elem_sz);
}

INLINE void stack_value_from_storage(stack_value_t* dst, uchar* storage, int index)
{
	stack_value_copy(dst, storage + index * STACK_VALUE_SIZE);
}

// builtin class fields, each fields...
uchar builtin_cls_delegate[] = { 2, ReferenceID, Int32 }; // number of fields, type of field1, type of field2....
uchar builtin_cls_list[] = { 4, ReferenceID, Int32, Int32, Int32 }; // storage, count, capacity, element type id
uchar builtin_cls_queue[] = { 6, ReferenceID, Int32, Int32, Int32, Int32, Int32 }; // storage, head, tail, count, capacity, element type id
uchar builtin_cls_stack[] = { 4, ReferenceID, Int32, Int32, Int32 }; // storage, count, capacity, element type id
uchar builtin_cls_dictionary[] = { 5, ReferenceID, Int32, Int32, Int32, Int32 }; // storage, count, capacity, key type, value type
uchar builtin_cls_hashset[] = { 4, ReferenceID, Int32, Int32, Int32 }; // storage, count, capacity, element type id
uchar builtin_cls_dis_handler[] = { 2, Int32, ReferenceID }; // len, storage(byte[])

#define BUILTIN_CLSIDX_ACTION 0
#define BUILTIN_CLSIDX_ACTION1 1
#define BUILTIN_CLSIDX_FUNC1 2
#define BUILTIN_CLSIDX_FUNC2 3
#define BUILTIN_CLSIDX_ACTION2 4
#define BUILTIN_CLSIDX_ACTION3 5
#define BUILTIN_CLSIDX_ACTION4 6
#define BUILTIN_CLSIDX_ACTION5 7
#define BUILTIN_CLSIDX_FUNC3 8
#define BUILTIN_CLSIDX_FUNC4 9
#define BUILTIN_CLSIDX_FUNC5 10
#define BUILTIN_CLSIDX_FUNC6 11
#define BUILTIN_CLSIDX_LIST 12
#define BUILTIN_CLSIDX_QUEUE 13
#define BUILTIN_CLSIDX_STACK 14
#define BUILTIN_CLSIDX_DICTIONARY 15
#define BUILTIN_CLSIDX_HASHSET 16
#define BUILTIN_CLSIDX_DIS 17

uchar* builtin_cls[] = {
	builtin_cls_delegate, // Action
	builtin_cls_delegate, // Action1
	builtin_cls_delegate, // Func1
	builtin_cls_delegate, // Func2
	builtin_cls_delegate, // Action2
	builtin_cls_delegate, // Action3
	builtin_cls_delegate, // Action4
	builtin_cls_delegate, // Action5
	builtin_cls_delegate, // Func3
	builtin_cls_delegate, // Func4
	builtin_cls_delegate, // Func5
	builtin_cls_delegate, // Func6
	builtin_cls_list,     // List`1
	builtin_cls_queue,    // Queue`1
	builtin_cls_stack,    // Stack`1
	builtin_cls_dictionary, // Dictionary`2
	builtin_cls_hashset,   // HashSet`1
	builtin_cls_dis_handler,   // DefaultInterpolatedStringHandler
};

#define LIST_FIELD_STORAGE 0
#define LIST_FIELD_COUNT 1
#define LIST_FIELD_CAPACITY 2
#define LIST_FIELD_ELEMENTTYPE 3
#define LIST_INITIAL_CAPACITY 4

INLINE int list_get_count(struct object_val* list_obj)
{
	return builtin_field_get_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_COUNT);
}

INLINE int list_get_capacity(struct object_val* list_obj)
{
	return builtin_field_get_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_CAPACITY);
}

INLINE void list_set_count(struct object_val* list_obj, int count)
{
	builtin_field_set_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_COUNT, count);
}

INLINE void list_set_capacity(struct object_val* list_obj, int capacity)
{
	builtin_field_set_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_CAPACITY, capacity);
}

INLINE int list_get_element_type(struct object_val* list_obj)
{
	return builtin_field_get_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_ELEMENTTYPE);
}

INLINE void list_set_element_type(struct object_val* list_obj, int type_id)
{
	builtin_field_set_int(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_ELEMENTTYPE, type_id);
}

INLINE int list_get_storage_ref(struct object_val* list_obj)
{
	return builtin_field_get_reference(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_STORAGE);
}

INLINE void list_set_storage_ref(struct object_val* list_obj, int ref_id)
{
	builtin_field_set_reference(list_obj, BUILTIN_CLSIDX_LIST, LIST_FIELD_STORAGE, ref_id);
}

INLINE uchar* list_storage_bytes(struct object_val* list_obj, struct array_val** out_arr)
{
	int storage_ref = list_get_storage_ref(list_obj);
	if (storage_ref == 0)
		return NULL;
	struct array_val* arr = expect_array(storage_ref, Byte, "List storage");
	if (out_arr) *out_arr = arr;
	return &arr->payload;
}

// Queue<T> helpers
#define QUEUE_FIELD_STORAGE 0
#define QUEUE_FIELD_HEAD 1
#define QUEUE_FIELD_TAIL 2
#define QUEUE_FIELD_COUNT 3
#define QUEUE_FIELD_CAPACITY 4
#define QUEUE_FIELD_ELEMENTTYPE 5

INLINE int queue_get_head(struct object_val* q) { return builtin_field_get_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_HEAD); }
INLINE int queue_get_tail(struct object_val* q) { return builtin_field_get_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_TAIL); }
INLINE int queue_get_count(struct object_val* q) { return builtin_field_get_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_COUNT); }
INLINE int queue_get_capacity(struct object_val* q) { return builtin_field_get_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_CAPACITY); }
INLINE int queue_get_element_type(struct object_val* q) { return builtin_field_get_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_ELEMENTTYPE); }
INLINE void queue_set_head(struct object_val* q, int v) { builtin_field_set_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_HEAD, v); }
INLINE void queue_set_tail(struct object_val* q, int v) { builtin_field_set_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_TAIL, v); }
INLINE void queue_set_count(struct object_val* q, int v) { builtin_field_set_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_COUNT, v); }
INLINE void queue_set_capacity(struct object_val* q, int v) { builtin_field_set_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_CAPACITY, v); }
INLINE void queue_set_element_type(struct object_val* q, int v) { builtin_field_set_int(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_ELEMENTTYPE, v); }
INLINE int queue_get_storage_ref(struct object_val* q) { return builtin_field_get_reference(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_STORAGE); }
INLINE void queue_set_storage_ref(struct object_val* q, int id) { builtin_field_set_reference(q, BUILTIN_CLSIDX_QUEUE, QUEUE_FIELD_STORAGE, id); }
INLINE uchar* queue_storage_bytes(struct object_val* q, struct array_val** out_arr) { int r = queue_get_storage_ref(q); if (r == 0) return NULL; struct array_val* a = expect_array(r, Byte, "Queue storage"); if (out_arr) *out_arr = a; return &a->payload; }

// Stack<T> helpers
#define STACK_FIELD_STORAGE 0
#define STACK_FIELD_COUNT 1
#define STACK_FIELD_CAPACITY 2
#define STACK_FIELD_ELEMENTTYPE 3

INLINE int stack_get_count(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_COUNT); }
INLINE int stack_get_capacity(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_CAPACITY); }
INLINE int stack_get_element_type(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_ELEMENTTYPE); }
INLINE void stack_set_count(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_COUNT, v); }
INLINE void stack_set_capacity(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_CAPACITY, v); }
INLINE void stack_set_element_type(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_ELEMENTTYPE, v); }
INLINE int stack_get_storage_ref(struct object_val* s) { return builtin_field_get_reference(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_STORAGE); }
INLINE void stack_set_storage_ref(struct object_val* s, int id) { builtin_field_set_reference(s, BUILTIN_CLSIDX_STACK, STACK_FIELD_STORAGE, id); }
INLINE uchar* stack_storage_bytes(struct object_val* s, struct array_val** out_arr) { int r = stack_get_storage_ref(s); if (r == 0) return NULL; struct array_val* a = expect_array(r, Byte, "Stack storage"); if (out_arr) *out_arr = a; return &a->payload; }

// Dictionary<TKey,TValue> helpers (linear-probe-like contiguous pairs)
#define DICT_FIELD_STORAGE 0
#define DICT_FIELD_COUNT 1
#define DICT_FIELD_CAPACITY 2
#define DICT_FIELD_KEYTYPE 3
#define DICT_FIELD_VALUETYPE 4

INLINE int dict_get_count(struct object_val* d) { return builtin_field_get_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_COUNT); }
INLINE int dict_get_capacity(struct object_val* d) { return builtin_field_get_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_CAPACITY); }
INLINE int dict_get_key_type(struct object_val* d) { return builtin_field_get_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_KEYTYPE); }
INLINE int dict_get_value_type(struct object_val* d) { return builtin_field_get_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_VALUETYPE); }
INLINE void dict_set_count(struct object_val* d, int v) { builtin_field_set_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_COUNT, v); }
INLINE void dict_set_capacity(struct object_val* d, int v) { builtin_field_set_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_CAPACITY, v); }
INLINE void dict_set_key_type(struct object_val* d, int v) { builtin_field_set_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_KEYTYPE, v); }
INLINE void dict_set_value_type(struct object_val* d, int v) { builtin_field_set_int(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_VALUETYPE, v); }
INLINE int dict_get_storage_ref(struct object_val* d) { return builtin_field_get_reference(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_STORAGE); }
INLINE void dict_set_storage_ref(struct object_val* d, int id) { builtin_field_set_reference(d, BUILTIN_CLSIDX_DICTIONARY, DICT_FIELD_STORAGE, id); }
INLINE uchar* dict_storage_bytes(struct object_val* d, struct array_val** out_arr) { int r = dict_get_storage_ref(d); if (r == 0) return NULL; struct array_val* a = expect_array(r, Byte, "Dict storage"); if (out_arr) *out_arr = a; return &a->payload; }

// HashSet<T> helpers
#define HSET_FIELD_STORAGE 0
#define HSET_FIELD_COUNT 1
#define HSET_FIELD_CAPACITY 2
#define HSET_FIELD_ELEMENTTYPE 3

INLINE int hset_get_count(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_COUNT); }
INLINE int hset_get_capacity(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_CAPACITY); }
INLINE int hset_get_element_type(struct object_val* s) { return builtin_field_get_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_ELEMENTTYPE); }
INLINE void hset_set_count(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_COUNT, v); }
INLINE void hset_set_capacity(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_CAPACITY, v); }
INLINE void hset_set_element_type(struct object_val* s, int v) { builtin_field_set_int(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_ELEMENTTYPE, v); }
INLINE int hset_get_storage_ref(struct object_val* s) { return builtin_field_get_reference(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_STORAGE); }
INLINE void hset_set_storage_ref(struct object_val* s, int id) { builtin_field_set_reference(s, BUILTIN_CLSIDX_HASHSET, HSET_FIELD_STORAGE, id); }
INLINE uchar* hset_storage_bytes(struct object_val* s, struct array_val** out_arr) { int r = hset_get_storage_ref(s); if (r == 0) return NULL; struct array_val* a = expect_array(r, Byte, "HashSet storage"); if (out_arr) *out_arr = a; return &a->payload; }


typedef struct
{
	int in_use;
	int buffer_ref_id;
	int capacity;
	int length;
} dis_handler_ctx;

//static dis_handler_ctx dis_handler_pool[DIS_HANDLER_MAX]; //never do this!

static int dis_clamp_capacity(int estimate);
static int dis_allocate_context(int estimate, const char* where);
static void dis_release_context(dis_handler_ctx* ctx);
static void dis_ensure_capacity(dis_handler_ctx* ctx, int extra);
static void dis_append_bytes(dis_handler_ctx* ctx, const char* data, int len);
static void dis_append_string_id(dis_handler_ctx* ctx, int str_id, const char* where);
static void dis_format_stack_value(dis_handler_ctx* ctx, stack_value_t* value, struct string_val* format, const char* where);
static dis_handler_ctx* dis_get_ctx_from_slot(uchar* slot, const char* where);

INLINE void push_int(uchar** reptr, int value) {
	PUSH_STACK_INT(value);
}

INLINE void push_float(uchar** reptr, float value) {
	PUSH_STACK_FLOAT_D(value);
}

INLINE void push_bool(uchar** reptr, bool value) {
	PUSH_STACK_INT8(value);
}

INLINE int pop_int(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == Int32, "Type mismatch: expected Int32, got %d", **reptr);
	return *(int*)(*reptr + 1);
}

INLINE float pop_float(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == Single, "Type mismatch: expected Single, got %d", **reptr);
	return *(float*)(*reptr + 1);
}

INLINE bool pop_bool(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == 0, "Type mismatch: expected Boolean, got %d", **reptr);
	return *(bool*)(*reptr + 1);
}

// New helper functions for other types
INLINE short pop_short(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == Int16, "Type mismatch: expected Int16, got %d", **reptr);
	return *(short*)(*reptr + 1);
}

INLINE char pop_sbyte(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == SByte, "Type mismatch: expected SByte, got %d", **reptr);
	return *(char*)(*reptr + 1);
}

INLINE unsigned char pop_byte(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == Byte, "Type mismatch: expected Byte, got %d", **reptr);
	return *(*reptr + 1);
}

INLINE long long pop_long(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == 9, "Type mismatch: expected Int64, got %d", **reptr);
	return *(long long*)(*reptr + 1);
}

INLINE int pop_reference(uchar** reptr) {
	POP;
	ASSERT_LANG(**reptr == ReferenceID, "Type mismatch: expected ReferenceID, got %d", **reptr);
	return *(int*)(*reptr + 1);
}

#include "additional_builtins.h"

// Built-in method implementations
void builtin_Object_ctor(uchar** reptr) {
	// Do nothing, as object constructor is empty
	POP;
}

// Math methods
void builtin_Math_Abs_Decimal(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, fabsf(value));
}

void builtin_Math_Abs_Double(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, fabsf(value));
}

void builtin_Math_Abs_Int16(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, abs((short)value));
}

void builtin_Math_Abs_Int32(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, abs(value));
}

void builtin_Math_Abs_Int64(uchar** reptr) {
	long long value = *(long long*)(*reptr - 9);
	POP;
	push_int(reptr, llabs(value));
}

void builtin_Math_Abs_SByte(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, abs((char)value));
}

void builtin_Math_Abs_Single(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, fabsf(value));
}

void builtin_Math_Acos(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, acosf(value));
}

void builtin_Math_Acosh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, acoshf(value));
}

void builtin_Math_Asin(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, asinf(value));
}

void builtin_Math_Asinh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, asinhf(value));
}

void builtin_Math_Atan(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, atanf(value));
}

void builtin_Math_Atan2(uchar** reptr) {
	float y = pop_float(reptr);
	float x = pop_float(reptr);
	push_float(reptr, atan2f(y, x));
}

void builtin_Math_Atanh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, atanhf(value));
}

void builtin_Math_Ceiling(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, ceilf(value));
}

void builtin_Math_Clamp_Double(uchar** reptr) {
	float max = pop_float(reptr);
	float min = pop_float(reptr);
	float value = pop_float(reptr);
	push_float(reptr, fmaxf(min, fminf(max, value)));
}

void builtin_Math_Clamp_Int16(uchar** reptr) {
	int max = pop_int(reptr);
	int min = pop_int(reptr);
	int value = pop_int(reptr);
	push_int(reptr, (short)(value < min ? min : (value > max ? max : value)));
}

void builtin_Math_Clamp_Int32(uchar** reptr) {
	int max = pop_int(reptr);
	int min = pop_int(reptr);
	int value = pop_int(reptr);
	push_int(reptr, value < min ? min : (value > max ? max : value));
}

void builtin_Math_Clamp_Int64(uchar** reptr) {
	long long max = *(long long*)(*reptr - 9);
	POP;
	long long min = *(long long*)(*reptr - 9);
	POP;
	long long value = *(long long*)(*reptr - 9);
	POP;
	push_int(reptr, (int)(value < min ? min : (value > max ? max : value)));
}

void builtin_Math_Clamp_SByte(uchar** reptr) {
	int max = pop_int(reptr);
	int min = pop_int(reptr);
	int value = pop_int(reptr);
	push_int(reptr, (char)(value < min ? min : (value > max ? max : value)));
}

void builtin_Math_Clamp_Single(uchar** reptr) {
	float max = pop_float(reptr);
	float min = pop_float(reptr);
	float value = pop_float(reptr);
	push_float(reptr, fmaxf(min, fminf(max, value)));
}

void builtin_Math_Cos(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, cosf(value));
}

void builtin_Math_Cosh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, coshf(value));
}

void builtin_Math_Exp(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, expf(value));
}

void builtin_Math_Floor(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, floorf(value));
}

void builtin_Math_Log(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, logf(value));
}

void builtin_Math_Log_Base(uchar** reptr) {
	float base = pop_float(reptr);
	float value = pop_float(reptr);
	push_float(reptr, logf(value) / logf(base));
}

void builtin_Math_Log10(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, log10f(value));
}

void builtin_Math_Log2(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, log2f(value));
}

void builtin_Math_Max_Double(uchar** reptr) {
	float b = pop_float(reptr);
	float a = pop_float(reptr);
	push_float(reptr, fmaxf(a, b));
}

void builtin_Math_Max_Int16(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, (short)(a > b ? a : b));
}

void builtin_Math_Max_Int32(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, a > b ? a : b);
}

void builtin_Math_Max_Int64(uchar** reptr) {
	long long b = *(long long*)(*reptr - 9);
	POP;
	long long a = *(long long*)(*reptr - 9);
	POP;
	push_int(reptr, (int)(a > b ? a : b));
}

void builtin_Math_Max_SByte(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, (char)(a > b ? a : b));
}

void builtin_Math_Max_Single(uchar** reptr) {
	float b = pop_float(reptr);
	float a = pop_float(reptr);
	push_float(reptr, fmaxf(a, b));
}

void builtin_Math_Min_Decimal(uchar** reptr) {
	float b = pop_float(reptr);
	float a = pop_float(reptr);
	push_float(reptr, fminf(a, b));
}

void builtin_Math_Min_Double(uchar** reptr) {
	float b = pop_float(reptr);
	float a = pop_float(reptr);
	push_float(reptr, fminf(a, b));
}

void builtin_Math_Min_Int16(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, (short)(a < b ? a : b));
}

void builtin_Math_Min_Int32(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, a < b ? a : b);
}

void builtin_Math_Min_Int64(uchar** reptr) {
	long long b = *(long long*)(*reptr - 9);
	POP;
	long long a = *(long long*)(*reptr - 9);
	POP;
	push_int(reptr, (int)(a < b ? a : b));
}

void builtin_Math_Min_SByte(uchar** reptr) {
	int b = pop_int(reptr);
	int a = pop_int(reptr);
	push_int(reptr, (char)(a < b ? a : b));
}

void builtin_Math_Min_Single(uchar** reptr) {
	float b = pop_float(reptr);
	float a = pop_float(reptr);
	push_float(reptr, fminf(a, b));
}

void builtin_Math_Pow(uchar** reptr) {
	float exponent = pop_float(reptr);
	float base = pop_float(reptr);
	push_float(reptr, powf(base, exponent));
}

void builtin_Math_Round(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, roundf(value));
}

void builtin_Math_Sign_Double(uchar** reptr) {
	float value = pop_float(reptr);
	push_int(reptr, value > 0 ? 1 : (value < 0 ? -1 : 0));
}

void builtin_Math_Sign_Int16(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, (short)value > 0 ? 1 : ((short)value < 0 ? -1 : 0));
}

void builtin_Math_Sign_Int32(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, value > 0 ? 1 : (value < 0 ? -1 : 0));
}

void builtin_Math_Sign_Int64(uchar** reptr) {
	long long value = *(long long*)(*reptr - 9);
	POP;
	push_int(reptr, value > 0 ? 1 : (value < 0 ? -1 : 0));
}

void builtin_Math_Sign_SByte(uchar** reptr) {
	int value = pop_int(reptr);
	push_int(reptr, (char)value > 0 ? 1 : ((char)value < 0 ? -1 : 0));
}

void builtin_Math_Sign_Single(uchar** reptr) {
	float value = pop_float(reptr);
	push_int(reptr, value > 0 ? 1 : (value < 0 ? -1 : 0));
}

void builtin_Math_Sin(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, sinf(value));
}

void builtin_Math_Sinh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, sinhf(value));
}

void builtin_Math_Sqrt(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, sqrtf(value));
}

void builtin_Math_Tan(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, tanf(value));
}
void builtin_Math_Tanh(uchar** reptr) {
	float value = pop_float(reptr);
	push_float(reptr, tanhf(value));
}

void format_string(char* format, char* result, int len_args, uchar** arg_ptr) {
    char* format_ptr = format;
    char* result_ptr = result;

    while (*format_ptr) {
        if (*format_ptr == '{') {
            char* end_brace = strchr(format_ptr, '}');
            if (end_brace) {
                // Extract the content between braces
                int content_len = end_brace - format_ptr - 1;
                if (content_len <= 10) {  // Reasonable limit for index + format
                    char content[12] = {0};
                    strncpy(content, format_ptr + 1, content_len);
                    
                    // Look for format specifier separator
                    char* format_sep = strchr(content, ':');
                    char* format_spec = NULL;
                    char index_str[4] = {0};
                    
                    if (format_sep) {
                        // We have a format specifier
                        *format_sep = '\0'; // Split the string
                        format_spec = format_sep + 1;
                        strncpy(index_str, content, sizeof(index_str) - 1);
                    } else {
                        // No format specifier
                        strncpy(index_str, content, sizeof(index_str) - 1);
                    }
                    
                    int index = atoi(index_str);

                    if (index >= 0 && index < len_args) {
                        uchar* heap_val_ptr = arg_ptr[index];
                    retry:
                        uchar type_id = *heap_val_ptr;
                        uchar* payload = &heap_val_ptr[1];  // Skip the header byte

                        // Build format specifier if provided
                        char sprintf_format[20] = {0};
                        
                        switch (type_id) {
                        case SByte:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(char*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(char*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%d", *(char*)payload);
                            }
                            break;
                        case Byte:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned char*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned char*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%u", *(unsigned char*)payload);
                            }
                            break;
                        case Int16:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(short*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(short*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%d", *(short*)payload);
                            }
                            break;
                        case UInt16:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned short*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned short*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%u", *(unsigned short*)payload);
                            }
                            break;
                        case Int32:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(int*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(int*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%d", *(int*)payload);
                            }
                            break;
                        case UInt32:
                            if (format_spec) {
                                if (format_spec[0] == 'X' || format_spec[0] == 'x') {
                                    // Hexadecimal format
                                    if (strlen(format_spec) > 1) {
                                        // Add padding if specified (e.g., X2)
                                        int width = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%0%d%s", width, format_spec[0] == 'X' ? "X" : "x");
                                    } else {
                                        sprintf(sprintf_format, "%%%s", format_spec[0] == 'X' ? "X" : "x");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned int*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(unsigned int*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%u", *(unsigned int*)payload);
                            }
                            break;
                        case Single:
                            if (format_spec) {
                                if (format_spec[0] == 'F' || format_spec[0] == 'f') {
                                    if (strlen(format_spec) > 1) {
                                        // Add precision if specified (e.g., F2)
                                        int precision = atoi(format_spec + 1);
                                        sprintf(sprintf_format, "%%.%df", precision);
                                    } else {
                                        sprintf(sprintf_format, "%%f");
                                    }
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(float*)payload);
                                } else {
                                    sprintf(sprintf_format, "%%%s", format_spec);
                                    result_ptr += sprintf(result_ptr, sprintf_format, *(float*)payload);
                                }
                            } else {
                                result_ptr += sprintf(result_ptr, "%g", *(float*)payload);
                            }
                            break;
                        case Boolean:
                            result_ptr += sprintf(result_ptr, "%s", *(bool*)payload ? "True" : "False");
                            break;
                        case Address:
                            result_ptr += sprintf(result_ptr, "<Address>");
                            break;
                        case JumpAddress:
                            result_ptr += sprintf(result_ptr, "<JumpAddress>");
                            break;
                        case ReferenceID:
                        {
                            int str_id = *(int*)payload;
                            if (str_id == 0) {
                                result_ptr += sprintf(result_ptr, "null");
                                break;
                            }
                            uchar* objh = heap_obj[str_id].pointer;
                            if (*objh == ArrayHeader)
                            {
                                result_ptr += sprintf(result_ptr, "<Array>");
                            }
                            else if (*objh == StringHeader) {
                                struct string_val* str = (struct string_val*)objh;
                                int len = str->str_len;
                                memcpy(result_ptr, &str->payload, len);
                                result_ptr += len;
                            }
                            else if (*objh == ObjectHeader)
                            {
                                result_ptr += sprintf(result_ptr, "<Object>");
                            }
                        }
                        break;
                        case MethodPointer:
                        {
                            struct method_pointer* mp = (struct method_pointer*)payload;
                            result_ptr += sprintf(result_ptr, "<Method: type=%d, id=%d>", mp->type, mp->id);
                        }
                        break;
                        case BoxedObject:
                        {
                            heap_val_ptr += 1;
                            goto retry;
                        }
                        default:
                            result_ptr += sprintf(result_ptr, "<Unsupported type: %d>", type_id);
                        }
                        format_ptr = end_brace + 1;
                        continue;
                    }
                }
            }
        }
        *result_ptr++ = *format_ptr++;
    }

    *result_ptr = '\0';  // Null-terminate the result string
}

static uchar* pop_value_type_slot(uchar** reptr, const char* where)
{
    POP;
    uchar* addr = *reptr;
    ASSERT_LANG(*addr == Address, "%s: expected Address for struct, got type %d", where, *addr);
	uchar target_type = TypedAddrGetType(addr);
	uchar* jmp = TypedAddrAsValPtr(addr);
	ASSERT_LANG(target_type == JumpAddress, "%s: expected JumpAddress to reference, got type %d", where, target_type);
    return mem0 + As(jmp, int);
}

void do_job(uchar** reptr, int len, uchar** arg_ptr)
{
	int format_str_id = pop_reference(reptr);
	ASSERT_LANG(format_str_id != 0, "format string is null");
	uchar* header = heap_obj[format_str_id].pointer;
	ASSERT_LANG(*header == StringHeader, "format argument is not a string (header=%d)", *header);

	char* format = &((struct string_val*)header)->payload;

	char result[256];  // Adjust size as needed
	format_string(format, result, len, arg_ptr);

	int result_str_id = newstr(strlen(result), (uchar*)result);
	PUSH_STACK_REFERENCEID(result_str_id);
}

// String methods
void builtin_String_Format_1(uchar** reptr) {
	uchar* args[1];
	POP;
	args[0] = *reptr;
	do_job(reptr, 1, args);
}

void builtin_String_Format_2(uchar** reptr) {
	uchar* args[2];
	POP;
	args[1] = *reptr;
	POP;
	args[0] = *reptr;
	do_job(reptr, 2, args);
}

void builtin_String_Format_3(uchar** reptr) {
	uchar* args[3];
	POP;
	args[2] = *reptr;
	POP;
	args[1] = *reptr;
	POP;
	args[0] = *reptr;
	do_job(reptr, 3, args);
}


void builtin_String_Format_Array(uchar** reptr) {
	int args_array_id = pop_reference(reptr);
	ASSERT_RT(args_array_id != 0, "format arguments array is null");
	uchar* args[16];
	uchar* header = heap_obj[args_array_id].pointer;
	ASSERT_LANG(*header == ArrayHeader, "format arguments is not an array (header=%d)", *header);
	struct array_val* arr = header;
	ASSERT_LANG(arr->typeid == BoxedObject, "format arguments array is not object[] (typeid=%d)", arr->typeid);
	for (int i = 0; i < arr->len; ++i)
		args[i] = &arr->payload + i * get_type_sz(BoxedObject);
	do_job(reptr, arr->len, args);
}

void builtin_String_Concat_2(uchar** reptr) {
	int str2_id = pop_reference(reptr);
	int str1_id = pop_reference(reptr);

	ASSERT_LANG(str1_id != 0 && str2_id != 0, "String.Concat arguments cannot be null");
	struct string_val* str1 = (struct string_val*)heap_obj[str1_id].pointer;
	struct string_val* str2 = (struct string_val*)heap_obj[str2_id].pointer;

	char result[128];

	int total_len = str1->str_len + str2->str_len;

	memcpy(result, &str1->payload, str1->str_len);
	memcpy(result + str1->str_len, &str2->payload, str2->str_len);
	result[total_len] = '\0';

	int result_str_id = newstr(total_len, (uchar*)result);

	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_String_Concat_3(uchar** reptr) {
	int str3_id = pop_reference(reptr);
	int str2_id = pop_reference(reptr);
	int str1_id = pop_reference(reptr);

	ASSERT_LANG(str1_id != 0 && str2_id != 0 && str3_id != 0, "String.Concat arguments cannot be null");
	struct string_val* str1 = (struct string_val*)heap_obj[str1_id].pointer;
	struct string_val* str2 = (struct string_val*)heap_obj[str2_id].pointer;
	struct string_val* str3 = (struct string_val*)heap_obj[str3_id].pointer;

	char result[256];

	int total_len = str1->str_len + str2->str_len + str3->str_len;

	memcpy(result, &str1->payload, str1->str_len);
	memcpy(result + str1->str_len, &str2->payload, str2->str_len);
	memcpy(result + str1->str_len + str2->str_len, &str3->payload, str3->str_len);
	result[total_len] = '\0';

	int result_str_id = newstr(total_len, (uchar*)result);

	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_String_Concat_4(uchar** reptr) {
	int str4_id = pop_reference(reptr);
	int str3_id = pop_reference(reptr);
	int str2_id = pop_reference(reptr);
	int str1_id = pop_reference(reptr);

	ASSERT_LANG(str1_id != 0 && str2_id != 0 && str3_id != 0 && str4_id != 0, "String.Concat arguments cannot be null");
	struct string_val* str1 = (struct string_val*)heap_obj[str1_id].pointer;
	struct string_val* str2 = (struct string_val*)heap_obj[str2_id].pointer;
	struct string_val* str3 = (struct string_val*)heap_obj[str3_id].pointer;
	struct string_val* str4 = (struct string_val*)heap_obj[str4_id].pointer;

	char result[512];

	int total_len = str1->str_len + str2->str_len + str3->str_len + str4->str_len;

	memcpy(result, &str1->payload, str1->str_len);
	memcpy(result + str1->str_len, &str2->payload, str2->str_len);
	memcpy(result + str1->str_len + str2->str_len, &str3->payload, str3->str_len);
	memcpy(result + str1->str_len + str2->str_len + str3->str_len, &str4->payload, str4->str_len);
	result[total_len] = '\0';

	int result_str_id = newstr(total_len, (uchar*)result);

	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_String_Substring_2(uchar** reptr) {
	int length = pop_int(reptr);
	int startIndex = pop_int(reptr);
	int str_id = pop_reference(reptr);

	ASSERT_RT(str_id != 0, "String.Substring called on null");
	struct string_val* str = (struct string_val*)heap_obj[str_id].pointer;
	ASSERT_LANG(*(uchar*)str == StringHeader, "String.Substring called on non-string (header=%d)", *(uchar*)str);

	if (startIndex < 0 || startIndex + length > str->str_len) {
		// Handle error: out of range
		PUSH_STACK_REFERENCEID(0);  // Push null or handle error as appropriate
		return;
	}

	int result_str_id = newstr(length, (uchar*)&str->payload + startIndex);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_String_get_Length(uchar** reptr) {
	int str_id = pop_reference(reptr);
	struct string_val* str = (struct string_val*)heap_obj[str_id].pointer;
	ASSERT_LANG(*(uchar*)str == StringHeader, "String.get_Length requires string");
	push_int(reptr, str->str_len);
}


void just_read(uchar** reptr, uchar type, uchar port, short ext)
{
	// Binary search for slot 
	int left = 0, right = processing_buf->N_slots - 1;
	unsigned int mysort = ((type << 24) | (port << 16) | ext); // little endian!
	while (left <= right) {
		int mid = left + (right - left) / 2;
		struct io_slot* sp = &processing_buf->slots[sorted_slots[mid]];
		if (sp->type == type && sp->aux0 == port && sp->aux1 == ext) {//just fuck.
			int refid = newarr(sp->len, Byte);
			struct array_val* arr = heap_obj[refid].pointer;
			memcpy(&arr->payload, &processing_buf->payload + sp->offset, sp->len);
			PUSH_STACK_REFERENCEID(refid);
			return;
		}
		else if (sp->sortable < mysort) {
			left = mid + 1;
		}
		else {
			right = mid - 1;
		}
	}
	PUSH_STACK_REFERENCEID(0); // ldnull
}

// RunOnMCU methods
void builtin_RunOnMCU_ReadStream(uchar** reptr) {
	int port = pop_int(reptr);
	just_read(reptr, STREAM_TYPE, port, 0);
}


void builtin_RunOnMCU_WriteStream(uchar** reptr) {
	int port = pop_int(reptr);
	int args_array_id = pop_reference(reptr);

	uchar* header = heap_obj[args_array_id].pointer;
	ASSERT_LANG(*header == ArrayHeader, "WriteStream data is not an array (header=%d)", *header);
	struct array_val* arr = header;
	ASSERT_LANG(arr->typeid == Byte, "WriteStream requires byte[] (typeid=%d)", arr->typeid);

	enter_critical();
	int n_offset = writing_buf->offset;
	writing_buf->offset += arr->len;
	leave_critical();

	memcpy(&writing_buf->payload + n_offset, &arr->payload, arr->len);

	write_stream(port, (&writing_buf->payload) + n_offset, arr->len);
}

void builtin_RunOnMCU_ReadEvent(uchar** reptr) {
	int event_id = pop_int(reptr);
	int port = pop_int(reptr);
	// Implement actual Event reading here

	just_read(reptr, EVENT_TYPE, port, event_id);
}

void builtin_RunOnMCU_WriteEvent(uchar** reptr) {
	int event_id = pop_int(reptr);
	int port = pop_int(reptr);
	int args_array_id = pop_reference(reptr);

	uchar* header = heap_obj[args_array_id].pointer;
	ASSERT_LANG(*header == ArrayHeader, "WriteEvent data is not an array (header=%d)", *header);
	struct array_val* arr = header;
	ASSERT_LANG(arr->typeid == Byte, "WriteEvent requires byte[] (typeid=%d)", arr->typeid);

	enter_critical();
	int n_offset = writing_buf->offset;
	writing_buf->offset += arr->len;
	leave_critical();

	memcpy(&writing_buf->payload + n_offset, &arr->payload, arr->len);

	write_event(port, event_id, (&writing_buf->payload) + n_offset, arr->len);
}

void builtin_RunOnMCU_ReadSnapshot(uchar** reptr) {
	// always have snapshot.
	struct io_slot* sp = &processing_buf->slots[sorted_slots[0]];
	int refid = newarr(sp->len, Byte);
	struct array_val* arr = heap_obj[refid].pointer;
	memcpy(&arr->payload, &processing_buf->payload + sp->offset, sp->len);
	PUSH_STACK_REFERENCEID(refid);
}

void builtin_RunOnMCU_WriteSnapshot(uchar** reptr) {
	int args_array_id = pop_reference(reptr);
	uchar* header = heap_obj[args_array_id].pointer;
	ASSERT_LANG(*header == ArrayHeader, "WriteSnapshot data is not an array (header=%d)", *header);
	struct array_val* arr = header;
	ASSERT_LANG(arr->typeid == Byte, "WriteSnapshot requires byte[] (typeid=%d)", arr->typeid);

	// don't have to have same snapshot layout.
	enter_critical();
	int n_offset = writing_buf->offset;
	writing_buf->offset += arr->len;
	leave_critical();

	memcpy(&writing_buf->payload + n_offset, &arr->payload, arr->len);

	write_snapshot(&writing_buf->payload + n_offset, arr->len);
}

void builtin_RunOnMCU_GetMicrosFromStart(uchar** reptr) {
	// Implement actual timing here
	push_int(reptr, get_cyclic_micros());
}
void builtin_RunOnMCU_GetMillisFromStart(uchar** reptr) {
	// Implement actual timing here
	push_int(reptr, get_cyclic_millis());
}
void builtin_RunOnMCU_GetSecondsFromStart(uchar** reptr) {
	// Implement actual timing here
	push_int(reptr, get_cyclic_seconds());
}

// ValueTuple constructors, it's generic so do type check.

// ValueTuple is struct, so may call newobj or just call.
void builtin_ValueTuple2_ctor(uchar** reptr) {

	struct stack_frame_header* my_stack = stack_ptr[new_stack_depth - 1];
	ASSERT_LANG(my_stack->evaluation_st_ptr <= *reptr, "ValueTuple2 ctor stack underflow");
	uchar* before = *reptr;
	POP;
	uchar* v2 = *reptr;
	POP;
	uchar* v1 = *reptr;

	struct object_val* tuple;
	if (!builtin_arg0) {
		tuple = pop_value_type_slot(reptr, "builtin_ValueTuple2_ctor");
	}
	else
	{
		tuple = heap_obj[builtin_arg0].pointer;
	}
	uchar* t1 = &tuple->payload;
	uchar* t2 = t1 + get_val_sz(*t1);

	copy_val(t1, v1); //stackptr actually.
	copy_val(t2, v2);

}

void builtin_ValueTuple3_ctor(uchar** reptr) {
	struct stack_frame_header* my_stack = stack_ptr[new_stack_depth - 1];
	ASSERT_LANG(my_stack->evaluation_st_ptr <= *reptr, "ValueTuple3 ctor stack underflow");

	POP;
	uchar* v3 = *reptr;
	POP;
	uchar* v2 = *reptr;
	POP;
	uchar* v1 = *reptr;

	struct object_val* tuple;
	if (!builtin_arg0) {
		tuple = pop_value_type_slot(reptr, "builtin_ValueTuple3_ctor");
	}
	else
	{
		tuple = heap_obj[builtin_arg0].pointer;
	}

	uchar* t1 = &tuple->payload;
	uchar* t2 = t1 + get_val_sz(*t1);
	uchar* t3 = t2 + get_val_sz(*t2);

	copy_val(t1, v1);
	copy_val(t2, v2);
	copy_val(t3, v3);
}

void builtin_ValueTuple4_ctor(uchar** reptr) {
	struct stack_frame_header* my_stack = stack_ptr[new_stack_depth - 1];
	ASSERT_LANG(my_stack->evaluation_st_ptr <= *reptr, "ValueTuple4 ctor stack underflow");

	POP;
	uchar* v4 = *reptr;
	POP;
	uchar* v3 = *reptr;
	POP;
	uchar* v2 = *reptr;
	POP;
	uchar* v1 = *reptr;

	struct object_val* tuple;
	if (!builtin_arg0) {
		tuple = pop_value_type_slot(reptr, "builtin_ValueTuple4_ctor");
	}
	else
	{
		tuple = heap_obj[builtin_arg0].pointer;
	}

	uchar* t1 = &tuple->payload;
	uchar* t2 = t1 + get_val_sz(*t1);
	uchar* t3 = t2 + get_val_sz(*t2);
	uchar* t4 = t3 + get_val_sz(*t3);

	copy_val(t1, v1);
	copy_val(t2, v2);
	copy_val(t3, v3);
	copy_val(t4, v4);
}

void builtin_RuntimeHelpers_InitializeArray(uchar** reptr) {
	POP;
	// address
	ASSERT_LANG(**reptr == Address, "InitializeArray requires address for arg2 (got type %d)", **reptr);
	uchar* addr = TypedAddrAsValPtr(*reptr);

	int array_id = pop_reference(reptr);

	struct array_val* array = (struct array_val*)heap_obj[array_id].pointer;

	memcpy(&array->payload, addr, get_type_sz(array->typeid) * array->len);
}

void builtin_Boolean_ToString(uchar** reptr) {
	bool value = pop_bool(reptr);
	const char* str = value ? "True" : "False";
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_Int32_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == 6 || typeid == Address, "Bad input type, got %d", typeid);

	int value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(int*)(ptr);
	}
	else value = *(int*)(*reptr + 1);

	char str[16];
#ifndef IS_MCU
	itoa(value, str, 10);
#else
	snprintf(str, sizeof(str), "%d", value);
#endif
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_Int16_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == 4 || typeid == Address, "Bad input type, got %d", typeid);

	short value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(short*)(ptr);
	}
	else value = *(short*)(*reptr + 1);

	char str[8];
#ifndef IS_MCU
	itoa(value, str, 10);
#else
	snprintf(str, sizeof(str), "%d", value);
#endif
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_Single_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == 8 || typeid == Address, "Bad input type, got %d", typeid);

	float value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(float*)(ptr);
	}
	else value = *(float*)(*reptr + 1);

	char str[16];
	snprintf(str, sizeof(str), "%g", value);
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_Byte_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == Byte || typeid == Address, "Bad input type, got %d", typeid);

	unsigned char value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(unsigned char*)(ptr);
	}
	else value = *(*reptr + 1);

	char str[4];
	snprintf(str, sizeof(str), "%u", value);
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_Char_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == 3 || typeid == Address, "Bad input type, got %d", typeid);

	unsigned short value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(unsigned short*)(ptr);
	}
	else value = *(unsigned short*)(*reptr + 1);

	char str[2];
	str[0] = (char)value;
	str[1] = '\0';
	int result_str_id = newstr(1, (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_UInt16_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == UInt16 || typeid == Address, "Bad input type, got %d", typeid);

	unsigned short value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(unsigned short*)(ptr);
	}
	else value = *(unsigned short*)(*reptr + 1);

	char str[8];
	snprintf(str, sizeof(str), "%u", value);
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void builtin_UInt32_ToString(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	ASSERT_LANG(typeid == UInt32 || typeid == Address, "Bad input type, got %d", typeid);

	unsigned int value;
	if (typeid == Address)
	{
		uchar* ptr = TypedAddrAsValPtr(*reptr);
		value = *(unsigned int*)(ptr);
	}
	else value = *(unsigned int*)(*reptr + 1);

	char str[16];
	snprintf(str, sizeof(str), "%u", value);
	int result_str_id = newstr(strlen(str), (uchar*)str);
	PUSH_STACK_REFERENCEID(result_str_id);
}

void delegate_ctor(uchar** reptr, unsigned short clsid)
{
    POP;
    ASSERT_LANG(**reptr == MethodPointer, "delegate_ctor requires method pointer");
    struct method_pointer* mp = (struct method_pointer*)(*reptr + 1);
	ASSERT_LANG(mp->type != 1 || mp->id < methods_N, "invalid custom method id_%d", mp->id);
	ASSERT_LANG(mp->type != 0, "builtin method as action not supported");
    int obj_id = pop_reference(reptr);

    // If target object is null (static delegate to instance method singleton), try to instantiate from method metadata
    if (obj_id == 0) {
        uchar* mptr = method_detail_pointer + methods_table[mp->id].meta_offset;
        uchar ret_type = *mptr; mptr += 1; // return type
        short ret_aux = *((short*)mptr); mptr += 2; (void)ret_type; (void)ret_aux;
        short n_args = *((short*)mptr); mptr += 2;
        if (n_args > 0) {
            uchar t0 = *mptr; short aux0 = *((short*)(mptr + 1));
            if (t0 == ReferenceID && aux0 >= 0) {
                obj_id = newobj(aux0);
            }
        }
    }

	// Set the fields of the Action
	struct object_val* del = (struct object_val*)heap_obj[builtin_arg0].pointer;
	uchar* heap = (&del->payload);
	ASSERT_LANG(del->clsid == clsid, "wrong type of clsid, expect %x, got %x", clsid, del->clsid);
	HEAP_WRITE_REFERENCEID(obj_id);
	HEAP_WRITE_INT(mp->id);
}

void delegate_ivk(uchar** reptr, unsigned short clsid, int argN)
{
    ASSERT_LANG(argN >= 0, "delegate ivk: bad argN");
    uchar* top = *reptr - STACK_STRIDE;
    uchar* lower = argN > 0 ? (*reptr - (argN + 1) * STACK_STRIDE) : (*reptr - STACK_STRIDE);
    ASSERT_LANG(lower >= stack_ptr[new_stack_depth - 1]->evaluation_st_ptr, "delegate ivk stack underflow");
    ASSERT_LANG(lower[0] == ReferenceID, "delegate ivk expects delegate ref on stack, got %d", lower[0]);
    int refid = *(int*)(lower + 1);
    struct object_val* action = cast_builtin_obj(refid);
	ASSERT_LANG(action->clsid == clsid, "wrong delegate type: expected %d got %d", clsid, action->clsid);

	// Extract the object and method pointer
	int this_id = *(int*)(&action->payload + 1);
	int method_id = *(int*)(&action->payload + get_val_sz(Int32) + 1);

    // Replace the delegate slot with the target object reference (this)
    lower[0] = ReferenceID;
    *(int*)(lower + 1) = this_id;

    DBG("delegate obj_%d invoke method_%d\n", refid, method_id);
    // Sync caller frame evaluation pointer before invoking
    stack_ptr[new_stack_depth - 1]->evaluation_pointer = *reptr;
    vm_push_stack(method_id, -1, reptr);

	// any ret val don't need to process here.
}

void builtin_Action_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf000);
}

void builtin_Action_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf000, 0);
}

void builtin_Action1_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf001);
}

void builtin_Action1_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf001, 1);
}

void builtin_Action2_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf004);
}

void builtin_Action2_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf004, 2);
}

void builtin_Action3_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf005);
}

void builtin_Action3_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf005, 3);
}

void builtin_Action4_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf006);
}

void builtin_Action4_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf006, 4);
}

void builtin_Action5_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf007);
}

void builtin_Action5_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf007, 5);
}

void builtin_Func1_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf002);
}

void builtin_Func1_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf002, 0);
}

void builtin_Func2_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf003);
}

void builtin_Func2_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf003, 1);
}

void builtin_Func3_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf008);
}

void builtin_Func3_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf008, 2);
}

void builtin_Func4_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf009);
}

void builtin_Func4_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf009, 3);
}

void builtin_Func5_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf00a);
}

void builtin_Func5_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf00a, 4);
}

void builtin_Func6_ctor(uchar** reptr) {
	delegate_ctor(reptr, 0xf00b);
}

void builtin_Func6_Invoke(uchar** reptr) {
	delegate_ivk(reptr, 0xf00b, 5);
}

void builtin_Console_WriteLine(uchar** reptr) {
	int refid = pop_reference(reptr);
	struct string_val* str = (struct string_val*)heap_obj[refid].pointer;
	print_line(&str->payload);
}

void builtin_BitConverter_GetBytes_Boolean(uchar** reptr) {
	bool value = pop_bool(reptr);
	int array_id = newarr(1, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	(&arr->payload)[0] = value ? 1 : 0;
	PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_Char(uchar** reptr) {
	short value = pop_short(reptr);  // Char is 16-bit in C#
	int array_id = newarr(2, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;

	*(short*)(&arr->payload) = value;
	PUSH_STACK_REFERENCEID(array_id);
}


void builtin_BitConverter_GetBytes_Int16(uchar** reptr) {
	short value = pop_short(reptr);
	int array_id = newarr(2, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	*(short*)(&arr->payload) = value;
	PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_Int32(uchar** reptr) {
	int value = pop_int(reptr);
	int array_id = newarr(4, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	*(int*)(&arr->payload) = value;
	PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_Single(uchar** reptr) {
	float value = pop_float(reptr);
	int array_id = newarr(4, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	*(int*)(&arr->payload) = *(int*)&value;
	PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_UInt16(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	PUSH_STACK_UINT16(*(unsigned short*)(&arr->payload + startIndex));
}

void builtin_BitConverter_GetBytes_UInt32(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_int(reptr, *(unsigned int*)(&arr->payload + startIndex));
}

void builtin_BitConverter_ToBoolean(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_bool(reptr, *(&arr->payload + startIndex) != 0);
}

void builtin_BitConverter_ToChar(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_int(reptr, *(short*)(&arr->payload + startIndex));  // Pushing as int since we don't have a specific PUSH for char
}


void builtin_BitConverter_ToInt16(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_int(reptr, *(short*)(&arr->payload + startIndex));
}

void builtin_BitConverter_ToInt32(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_int(reptr, *(int*)(&arr->payload + startIndex));
}

void builtin_BitConverter_ToSingle(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	float value;
	*(int*)&value = *(int*)(&arr->payload + startIndex);
	push_float(reptr, value);
}

void builtin_BitConverter_ToUInt16(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	PUSH_STACK_UINT16(*(unsigned short*)(&arr->payload + startIndex));
}

void builtin_BitConverter_ToUInt32(uchar** reptr) {
	int startIndex = pop_int(reptr);
	int array_id = pop_reference(reptr);
	struct array_val* arr = heap_obj[array_id].pointer;
	push_int(reptr, *(unsigned int*)(&arr->payload + startIndex));
}



// String.Join implementation for IEnumerable<T>
void builtin_String_Join_IEnumerable(uchar** reptr) {
	// Pop the enumerable (array) from the stack
	int array_id = pop_reference(reptr);

	// Pop the separator string from the stack
	int separator_id = pop_reference(reptr);

	if (array_id == 0) {
		// Handle null array - return empty string
		int empty_str_id = newstr(0, (uchar*)"");
		PUSH_STACK_REFERENCEID(empty_str_id);
		return;
	}

	if (separator_id == 0) {
		// Handle null separator - use empty string as separator
		separator_id = newstr(0, (uchar*)"");
	}

	uchar* array_header = heap_obj[array_id].pointer;
	if (*array_header != ArrayHeader) {
		ASSERT_LANG(0, "String.Join expects an array, got type %d", *array_header);
	}

	struct array_val* arr = (struct array_val*)array_header;
	struct string_val* separator = (struct string_val*)heap_obj[separator_id].pointer;

	if (arr->len == 0) {
		// Return empty string for empty array
		int empty_str_id = newstr(0, (uchar*)"");
		PUSH_STACK_REFERENCEID(empty_str_id);
		return;
	}

	// First pass: calculate total length
	int total_length = 0;
	int valid_items = 0;

    for (int i = 0; i < arr->len; i++) {
        if (arr->typeid == ReferenceID) {
			int* elem_ptr = (int*)(&arr->payload + i * get_type_sz(ReferenceID));
			int item_id = *elem_ptr;

			if (item_id != 0) { // Skip null items
				uchar* item_header = heap_obj[item_id].pointer;
				if (*item_header == StringHeader) {
					struct string_val* str = (struct string_val*)item_header;
					total_length += str->str_len;
					valid_items++;
				}
				else {
					// For non-string objects, assume a placeholder size
					total_length += 9; // "[Object]"
					valid_items++;
				}
			}
        }
        else {
            // For numeric/value arrays, estimate decimal string length (worst-case for Int32)
            total_length += 11; // sign + 10 digits
            valid_items++;
        }
	}

	// Add separators length
	if (valid_items > 1) {
		total_length += (valid_items - 1) * separator->str_len;
	}

	// Allocate buffer for the result
	char result[256]; // Fixed buffer for simplicity, adjust as needed
	if (total_length > 255) {
		total_length = 255; // Limit to buffer size
	}

	// Second pass: build the string
	int offset = 0;
	int items_added = 0;

    for (int i = 0; i < arr->len && offset < 255; i++) {
        if (arr->typeid == ReferenceID) {
			int* elem_ptr = (int*)(&arr->payload + i * get_type_sz(ReferenceID));
			int item_id = *elem_ptr;

			if (item_id != 0) { // Skip null items
				uchar* item_header = heap_obj[item_id].pointer;

				// Add separator if not the first item
				if (items_added > 0 && separator->str_len > 0) {
					int sep_len = separator->str_len;
					if (offset + sep_len > 255) sep_len = 255 - offset;
					memcpy(result + offset, &separator->payload, sep_len);
					offset += sep_len;
					if (offset >= 255) break;
				}

				if (*item_header == StringHeader) {
					struct string_val* str = (struct string_val*)item_header;
					int str_len = str->str_len;
					if (offset + str_len > 255) str_len = 255 - offset;
					memcpy(result + offset, &str->payload, str_len);
					offset += str_len;
					items_added++;
				}
				else {
					// Handle non-string objects with a placeholder
					const char* placeholder = "[Object]";
					int plc_len = 8;
					if (offset + plc_len > 255) plc_len = 255 - offset;
					memcpy(result + offset, placeholder, plc_len);
					offset += plc_len;
					items_added++;
				}
			}
        }
        else {
            // Add separator if not the first item
            if (items_added > 0 && separator->str_len > 0) {
                int sep_len = separator->str_len;
                if (offset + sep_len > 255) sep_len = 255 - offset;
                memcpy(result + offset, &separator->payload, sep_len);
                offset += sep_len;
                if (offset >= 255) break;
            }

            // Convert primitive value to string (support Int32 for now)
            if (arr->typeid == Int32) {
                int value = *(int*)(&arr->payload + i * get_type_sz(Int32));
                char buf[16];
                int len = snprintf(buf, sizeof(buf), "%d", value);
                if (len < 0) len = 0; if (len > 255 - offset) len = 255 - offset;
                memcpy(result + offset, buf, len);
                offset += len;
            } else {
                // Fallback placeholder for unsupported primitives
                const char* placeholder = "[Value]";
                int plc_len = 7;
                if (offset + plc_len > 255) plc_len = 255 - offset;
                memcpy(result + offset, placeholder, plc_len);
                offset += plc_len;
            }
            items_added++;
        }
	}

	result[offset] = '\0';

	// Create a new string and push it onto the stack
	int result_str_id = newstr(offset, (uchar*)result);
	PUSH_STACK_REFERENCEID(result_str_id);
}

// String.Join implementation for Object[]
void builtin_String_Join_ObjectArray(uchar** reptr) {
	// Pop the object array from the stack
	int array_id = pop_reference(reptr);

	// Pop the separator string from the stack
	int separator_id = pop_reference(reptr);

	if (array_id == 0) {
		// Handle null array - return empty string
		int empty_str_id = newstr(0, (uchar*)"");
		PUSH_STACK_REFERENCEID(empty_str_id);
		return;
	}

	if (separator_id == 0) {
		// Handle null separator - use empty string as separator
		separator_id = newstr(0, (uchar*)"");
	}

	uchar* array_header = heap_obj[array_id].pointer;
	if (*array_header != ArrayHeader) {
		ASSERT_LANG(0, "String.Join expects an array, got type %d", *array_header);
	}

	struct array_val* arr = (struct array_val*)array_header;
	struct string_val* separator = (struct string_val*)heap_obj[separator_id].pointer;

	if (arr->len == 0) {
		// Return empty string for empty array
		int empty_str_id = newstr(0, (uchar*)"");
		PUSH_STACK_REFERENCEID(empty_str_id);
		return;
	}

	// Use same logic as for IEnumerable but handle boxed objects
	char result[256]; // Fixed buffer for simplicity
	int offset = 0;
	int items_added = 0;

	for (int i = 0; i < arr->len && offset < 255; i++) {
		uchar* elem_ptr;
		int item_id = 0;

		if (arr->typeid == ReferenceID) {
			elem_ptr = &arr->payload + i * get_type_sz(ReferenceID);
			item_id = *(int*)elem_ptr;
		}
		else if (arr->typeid == BoxedObject) {
			elem_ptr = &arr->payload + i * get_type_sz(BoxedObject);
			if (*elem_ptr == ReferenceID) {
				item_id = *(int*)(elem_ptr + 1);
			}
		}

		// Add separator if not the first item
		if (items_added > 0 && separator->str_len > 0) {
			int sep_len = separator->str_len;
			if (offset + sep_len > 255) sep_len = 255 - offset;
			memcpy(result + offset, &separator->payload, sep_len);
			offset += sep_len;
			if (offset >= 255) break;
		}

		if (item_id != 0) {
			uchar* item_header = heap_obj[item_id].pointer;
			if (*item_header == StringHeader) {
				struct string_val* str = (struct string_val*)item_header;
				int str_len = str->str_len;
				if (offset + str_len > 255) str_len = 255 - offset;
				memcpy(result + offset, &str->payload, str_len);
				offset += str_len;
			}
			else {
				// Handle non-string objects
				const char* placeholder = "[Object]";
				int plc_len = 8;
				if (offset + plc_len > 255) plc_len = 255 - offset;
				memcpy(result + offset, placeholder, plc_len);
				offset += plc_len;
			}
		}
		else {
			// Handle null
			const char* placeholder = "";
			memcpy(result + offset, placeholder, 0);
		}

		items_added++;
	}

	result[offset] = '\0';

	// Create a new string and push it onto the stack
	int result_str_id = newstr(offset, (uchar*)result);
	PUSH_STACK_REFERENCEID(result_str_id);
}

// Implementation of Enumerable.Select (supports primitive and reference results)
void builtin_Enumerable_Select(uchar** reptr) {
    int selector_id = pop_reference(reptr); // Func<TSource,TResult>
    int source_id = pop_reference(reptr);   // IEnumerable<TSource>

    if (source_id == 0 || selector_id == 0) { PUSH_STACK_REFERENCEID(0); return; }

    uchar* source_header = heap_obj[source_id].pointer;
    bool is_list = false;
    struct array_val* source_arr = 0;
    struct object_val* list_obj = 0;
    if (*source_header == ArrayHeader) {
        source_arr = (struct array_val*)source_header;
    } else if (*source_header == ObjectHeader) {
        struct object_val* obj = (struct object_val*)source_header;
        if (obj->clsid == BUILTIN_CLSID(BUILTIN_CLSIDX_LIST)) { is_list = true; list_obj = obj; }
    } else {
		ASSERT_LANG(0, "Enumerable.Select expects array or List source, got type %d", *source_header);
    }
    struct object_val* selector = (struct object_val*)heap_obj[selector_id].pointer;
    ASSERT_LANG(selector->clsid == 0xf003, "Enumerable.Select expects a Func<T, TResult> delegate, got class ID %d", selector->clsid);

    int delegate_this_id = *(int*)(&selector->payload + 1);
    int delegate_method_id = *(int*)(&selector->payload + get_val_sz(ReferenceID) + 1);

    int src_len = is_list ? list_get_count(list_obj) : (source_arr ? source_arr->len : 0);
    if (src_len == 0) { // empty => empty array of Int32 by default
        int rid = newarr(0, Int32);
        PUSH_STACK_REFERENCEID(rid);
        return;
    }

    // Execute delegate on first element to detect result type
    uchar* saved_sp = *reptr;
    if (delegate_this_id > 0) PUSH_STACK_REFERENCEID(delegate_this_id);
    if (!is_list) {
        if (source_arr->typeid == ReferenceID) {
            int element_id = *(int*)(&source_arr->payload + 0 * get_type_sz(ReferenceID));
            PUSH_STACK_REFERENCEID(element_id);
        } else {
            **reptr = source_arr->typeid;
            memcpy(*reptr + 1, &source_arr->payload + 0 * get_type_sz(source_arr->typeid), get_type_sz(source_arr->typeid));
            *reptr += STACK_STRIDE;
        }
    } else {
        struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
        push_stack_value(reptr, (stack_value_t*)(storage + 0 * STACK_STRIDE));
    }
    vm_push_stack(delegate_method_id, -1, reptr);
    POP;
    uchar result_type = **reptr;
    int result_elem_size = get_type_sz(result_type);

    // Allocate result array using detected element type
    int result_arr_id = newarr(src_len, result_type == ReferenceID ? ReferenceID : result_type);
    struct array_val* result_arr = (struct array_val*)heap_obj[result_arr_id].pointer;

    // Store first element
    if (result_type == ReferenceID) {
        int result_id = *(int*)(*reptr + 1);
        *(int*)(&result_arr->payload + 0 * get_type_sz(ReferenceID)) = result_id;
    } else {
        memcpy(&result_arr->payload + 0 * result_elem_size, *reptr + 1, result_elem_size);
    }
    *reptr = saved_sp;

    // Process remaining elements
    for (int i = 1; i < src_len; i++) {
        uchar* current_stack_ptr = *reptr;
        if (delegate_this_id > 0) PUSH_STACK_REFERENCEID(delegate_this_id);
        if (!is_list) {
            if (source_arr->typeid == ReferenceID) {
                int element_id = *(int*)(&source_arr->payload + i * get_type_sz(ReferenceID));
                PUSH_STACK_REFERENCEID(element_id);
            } else {
                **reptr = source_arr->typeid;
                memcpy(*reptr + 1, &source_arr->payload + i * get_type_sz(source_arr->typeid), get_type_sz(source_arr->typeid));
                *reptr += STACK_STRIDE;
            }
        } else {
            struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
            push_stack_value(reptr, (stack_value_t*)(storage + i * STACK_STRIDE));
        }
        vm_push_stack(delegate_method_id, -1, reptr);
        POP;
        ASSERT_RT(**reptr == result_type, "Select: result type changed within sequence (%d->%d)", result_type, **reptr);
        if (result_type == ReferenceID) {
            int result_id = *(int*)(*reptr + 1);
            *(int*)(&result_arr->payload + i * get_type_sz(ReferenceID)) = result_id;
        } else {
            memcpy(&result_arr->payload + i * result_elem_size, *reptr + 1, result_elem_size);
        }
        *reptr = current_stack_ptr;
    }

    PUSH_STACK_REFERENCEID(result_arr_id);
}

// List<T> builtin methods
void builtin_List_ctor(uchar** reptr) {
	// newobj passes object via builtin_arg0; do not pop 'this' here
	struct object_val* list_obj = expect_builtin_obj(builtin_arg0, BUILTIN_CLSIDX_LIST, "List.ctor");
	// initialize with small byte-array storage; element type is unknown until first Add/Item set
	int storage_id = newarr(LIST_INITIAL_CAPACITY * STACK_STRIDE, Byte);
	list_set_storage_ref(list_obj, storage_id);
	list_set_count(list_obj, 0);
	list_set_capacity(list_obj, LIST_INITIAL_CAPACITY);
	// element type 0 means unknown; will be set on first write
	list_set_element_type(list_obj, 0);
}

void builtin_List_Add(uchar** reptr) {
	stack_value_t value;
	POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = cast_builtin_obj(this_id);

	int count = list_get_count(list_obj);
	int capacity = list_get_capacity(list_obj);
	int storage_ref = list_get_storage_ref(list_obj);
	struct array_val* storage_arr = cast_array(storage_ref);
	uchar* storage = &storage_arr->payload;

	int elem_type = list_get_element_type(list_obj);
	uchar val_type = stack_value_type(&value);
	if (elem_type == 0) {
		list_set_element_type(list_obj, val_type);
		elem_type = val_type;
	}
	ASSERT_LANG(elem_type == val_type, "List.Add type mismatch: list elem=%d, value=%d", elem_type, val_type);

	if (count >= capacity) {
		int new_capacity = capacity << 1;  // Bit shift is faster than multiply
		int new_storage_ref = newarr(new_capacity * STACK_STRIDE, Byte);
		struct array_val* new_arr = cast_array(new_storage_ref);
		if (count > 0) memcpy(&new_arr->payload, storage, count * STACK_STRIDE);
		list_set_storage_ref(list_obj, new_storage_ref);
		list_set_capacity(list_obj, new_capacity);
		storage = &new_arr->payload;
	}

	stack_value_store(storage + count * STACK_STRIDE, &value);
	list_set_count(list_obj, count + 1);
}

void builtin_List_get_Count(uchar** reptr) {
	int this_id = pop_reference(reptr);
	push_int(reptr, list_get_count(cast_builtin_obj(this_id)));
}

void builtin_List_get_Item(uchar** reptr) {
	int index = pop_int(reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = cast_builtin_obj(this_id);
	int count = list_get_count(list_obj);
	ASSERT_RT(index >= 0 && index < count, "List index out of range: %d/%d", index, count);
	struct array_val* storage_arr;
	uchar* storage = list_storage_bytes(list_obj, &storage_arr);
	stack_value_t tmp;
	stack_value_from_storage(&tmp, storage, index);
	push_stack_value(reptr, &tmp);
}

void builtin_List_set_Item(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int index = pop_int(reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = cast_builtin_obj(this_id);
	int count = list_get_count(list_obj);
	ASSERT_RT(index >= 0 && index < count, "List index out of range: %d/%d", index, count);
	int elem_type = list_get_element_type(list_obj);
	uchar val_type = stack_value_type(&value);
	if (elem_type == 0) { list_set_element_type(list_obj, val_type); elem_type = val_type; }
	ASSERT_LANG(elem_type == val_type, "List.set_Item type mismatch: %d vs %d", elem_type, val_type);
	struct array_val* storage_arr;
	uchar* storage = list_storage_bytes(list_obj, &storage_arr);
	stack_value_store(storage + index * STACK_STRIDE, &value);
}

void builtin_List_RemoveAt(uchar** reptr) {
	// stack: index(Int32), this(List`1)
	int index = pop_int(reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = expect_builtin_obj(this_id, BUILTIN_CLSIDX_LIST, "List.RemoveAt");
	int count = list_get_count(list_obj);
	ASSERT_RT(index >= 0 && index < count, "List.RemoveAt index out of range: %d/%d", index, count);
	struct array_val* storage_arr;
	uchar* storage = list_storage_bytes(list_obj, &storage_arr);
	int tail = count - 1;
	if (index < tail) {
		memmove(storage + index * STACK_STRIDE, storage + (index + 1) * STACK_STRIDE, (tail - index) * STACK_STRIDE);
	}
	list_set_count(list_obj, count - 1);
}

void builtin_List_Clear(uchar** reptr) {
	// stack: this(List`1)
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = expect_builtin_obj(this_id, BUILTIN_CLSIDX_LIST, "List.Clear");
	list_set_count(list_obj, 0);
}

void builtin_List_Contains(uchar** reptr) {
	// stack: value(T), this(List`1)
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = expect_builtin_obj(this_id, BUILTIN_CLSIDX_LIST, "List.Contains");
	int count = list_get_count(list_obj);
	struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
	uchar elem_type = (uchar)list_get_element_type(list_obj);
	uchar val_type = stack_value_type(&value);
	if (elem_type != 0 && elem_type != val_type) { push_bool(reptr, false); return; }
	for (int i = 0; i < count; ++i) {
		if (memcmp(storage + i * STACK_STRIDE, value.bytes, STACK_STRIDE) == 0) { push_bool(reptr, true); return; }
	}
	push_bool(reptr, false);
}

void builtin_List_IndexOf(uchar** reptr) {
	// stack: value(T), this(List`1)
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = expect_builtin_obj(this_id, BUILTIN_CLSIDX_LIST, "List.IndexOf");
	int count = list_get_count(list_obj);
	struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
	for (int i = 0; i < count; ++i) {
		if (memcmp(storage + i * STACK_STRIDE, value.bytes, STACK_STRIDE) == 0) { push_int(reptr, i); return; }
	}
	push_int(reptr, -1);
}

void builtin_List_InsertRange(uchar** reptr) {
	// stack: collection(IEnumerable`1 -> we expect array), index(Int32), this(List`1)
	int source_id = pop_reference(reptr);
	int index = pop_int(reptr);
	int this_id = pop_reference(reptr);
	struct object_val* list_obj = expect_builtin_obj(this_id, BUILTIN_CLSIDX_LIST, "List.InsertRange");
	if (source_id == 0) return;
	struct array_val* src = expect_array(source_id, 0xFF, "List.InsertRange src");
	int insert_count = src->len;
	if (insert_count == 0) return;
	int count = list_get_count(list_obj);
	ASSERT_RT(index >= 0 && index <= count, "List.InsertRange index out of range: %d/%d", index, count);

	// If element type not set, set from first element of source
	int elem_type = list_get_element_type(list_obj);
	if (elem_type == 0) { list_set_element_type(list_obj, src->typeid == ReferenceID ? ReferenceID : src->typeid); elem_type = list_get_element_type(list_obj); }
	ASSERT_RT(src->typeid == elem_type, "List.InsertRange type mismatch: list=%d src=%d", elem_type, src->typeid);

	int capacity = list_get_capacity(list_obj);
	int needed = count + insert_count;
	int storage_ref = list_get_storage_ref(list_obj);
	struct array_val* storage_arr = expect_array(storage_ref, Byte, "List.InsertRange storage");
	uchar* storage = &storage_arr->payload;
	if (needed > capacity) {
		int new_capacity = capacity * 2; if (new_capacity < needed) new_capacity = needed;
		int new_storage_ref = newarr(new_capacity * STACK_STRIDE, Byte);
		struct array_val* new_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		memcpy(&new_arr->payload, storage, count * STACK_STRIDE);
		list_set_storage_ref(list_obj, new_storage_ref);
		list_set_capacity(list_obj, new_capacity);
		storage_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		storage = &storage_arr->payload;
	}
	// make room
	memmove(storage + (index + insert_count) * STACK_STRIDE, storage + index * STACK_STRIDE, (count - index) * STACK_STRIDE);
	// copy from src array into list storage as stack values
	int elem_sz = get_type_sz(src->typeid);
	for (int i = 0; i < insert_count; ++i) {
		stack_value_t tmp;
		stack_value_clear(&tmp);
		tmp.bytes[0] = src->typeid;
		memcpy(tmp.bytes + 1, &src->payload + i * elem_sz, elem_sz);
		stack_value_store(storage + (index + i) * STACK_STRIDE, &tmp);
	}
	list_set_count(list_obj, needed);
}

// Queue<T> builtin methods
void builtin_Queue_ctor(uchar** reptr) {
	// newobj passes object via builtin_arg0; do not pop 'this' here
	struct object_val* q = expect_builtin_obj(builtin_arg0, BUILTIN_CLSIDX_QUEUE, "Queue.ctor");
	int storage_id = newarr(LIST_INITIAL_CAPACITY * STACK_STRIDE, Byte);
	queue_set_storage_ref(q, storage_id);
	queue_set_head(q, 0);
	queue_set_tail(q, 0);
	queue_set_count(q, 0);
	queue_set_capacity(q, LIST_INITIAL_CAPACITY);
	queue_set_element_type(q, 0);
}

void builtin_Queue_Enqueue(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* q = cast_builtin_obj(this_id);
	int count = queue_get_count(q);
	int capacity = queue_get_capacity(q);
	int head = queue_get_head(q);
	int tail = queue_get_tail(q);
	int elem_type = queue_get_element_type(q);
	uchar val_type = stack_value_type(&value);
	if (elem_type == 0) { queue_set_element_type(q, val_type); elem_type = val_type; }
	ASSERT_RT(elem_type == val_type, "Queue.Enqueue type mismatch: %d vs %d", elem_type, val_type);
	struct array_val* storage_arr; uchar* storage = queue_storage_bytes(q, &storage_arr);
	if (count >= capacity) {
		int new_capacity = capacity * 2;
		int new_storage_ref = newarr(new_capacity * STACK_STRIDE, Byte);
		struct array_val* new_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		uchar* ns = &new_arr->payload;
		for (int i = 0; i < count; ++i) {
			int idx = (head + i) % capacity;
			memcpy(ns + i * STACK_STRIDE, storage + idx * STACK_STRIDE, STACK_STRIDE);
		}
		queue_set_storage_ref(q, new_storage_ref);
		queue_set_capacity(q, new_capacity);
		head = 0; tail = count;
		queue_set_head(q, head); queue_set_tail(q, tail);
		storage_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		storage = &storage_arr->payload;
		capacity = new_capacity;
	}
	stack_value_store(storage + tail * STACK_STRIDE, &value);
	tail = (tail + 1) % capacity;
	queue_set_tail(q, tail);
	queue_set_count(q, count + 1);
}

void builtin_Queue_Dequeue(uchar** reptr) {
	int this_id = pop_reference(reptr);
	struct object_val* q = cast_builtin_obj(this_id);
	int count = queue_get_count(q);
	ASSERT_RT(count > 0, "Queue is empty");
	int capacity = queue_get_capacity(q);
	int head = queue_get_head(q);
	struct array_val* storage_arr; uchar* storage = queue_storage_bytes(q, &storage_arr);
	stack_value_t tmp; stack_value_copy(&tmp, storage + head * STACK_STRIDE);
	head = (head + 1) % capacity; queue_set_head(q, head);
	queue_set_count(q, count - 1);
	push_stack_value(reptr, &tmp);
}

void builtin_Queue_Peek(uchar** reptr) {
	int this_id = pop_reference(reptr);
	struct object_val* q = cast_builtin_obj(this_id);
	int count = queue_get_count(q);
	ASSERT_RT(count > 0, "Queue is empty");
	int head = queue_get_head(q);
	struct array_val* storage_arr; uchar* storage = queue_storage_bytes(q, &storage_arr);
	stack_value_t tmp; stack_value_copy(&tmp, storage + head * STACK_STRIDE);
	push_stack_value(reptr, &tmp);
}

void builtin_Queue_get_Count(uchar** reptr) {
	int this_id = pop_reference(reptr);
	push_int(reptr, queue_get_count(cast_builtin_obj(this_id)));
}

// Stack<T> builtin methods
void builtin_Stack_ctor(uchar** reptr) {
	// newobj passes object via builtin_arg0; do not pop 'this' here
	struct object_val* s = expect_builtin_obj(builtin_arg0, BUILTIN_CLSIDX_STACK, "Stack.ctor");
	int storage_id = newarr(LIST_INITIAL_CAPACITY * STACK_STRIDE, Byte);
	stack_set_storage_ref(s, storage_id);
	stack_set_count(s, 0);
	stack_set_capacity(s, LIST_INITIAL_CAPACITY);
	stack_set_element_type(s, 0);
}

void builtin_Stack_Push(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = stack_get_count(s);
	int capacity = stack_get_capacity(s);
	int elem_type = stack_get_element_type(s);
	uchar val_type = stack_value_type(&value);
	if (elem_type == 0) { stack_set_element_type(s, val_type); elem_type = val_type; }
	ASSERT_LANG(elem_type == val_type, "Stack.Push type mismatch: %d vs %d", elem_type, val_type);
	struct array_val* storage_arr; uchar* storage = stack_storage_bytes(s, &storage_arr);
	if (count >= capacity) {
		int new_capacity = capacity * 2;
		int new_storage_ref = newarr(new_capacity * STACK_STRIDE, Byte);
		struct array_val* new_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		memcpy(&new_arr->payload, storage, capacity * STACK_STRIDE);
		stack_set_storage_ref(s, new_storage_ref);
		stack_set_capacity(s, new_capacity);
		storage_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		storage = &storage_arr->payload;
	}
	stack_value_store(storage + count * STACK_STRIDE, &value);
	stack_set_count(s, count + 1);
}

void builtin_Stack_Pop(uchar** reptr) {
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = stack_get_count(s);
	ASSERT_RT(count > 0, "Stack is empty");
	struct array_val* storage_arr; uchar* storage = stack_storage_bytes(s, &storage_arr);
	stack_value_t tmp; stack_value_copy(&tmp, storage + (count - 1) * STACK_STRIDE);
	stack_set_count(s, count - 1);
	push_stack_value(reptr, &tmp);
}

void builtin_Stack_Peek(uchar** reptr) {
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = stack_get_count(s);
	ASSERT_RT(count > 0, "Stack is empty");
	struct array_val* storage_arr; uchar* storage = stack_storage_bytes(s, &storage_arr);
	stack_value_t tmp; stack_value_copy(&tmp, storage + (count - 1) * STACK_STRIDE);
	push_stack_value(reptr, &tmp);
}

void builtin_Stack_get_Count(uchar** reptr) {
	int this_id = pop_reference(reptr);
	push_int(reptr, stack_get_count(cast_builtin_obj(this_id)));
}

// Dictionary<TKey,TValue> builtin methods
static int dict_find_index(struct object_val* d, uchar* storage, int count, const stack_value_t* key) {
	uchar key_type = key->bytes[0];
	for (int i = 0; i < count; ++i) {
		uchar* kp = storage + (i * 2) * STACK_STRIDE;
		// Compare type first
		if (kp[0] != key_type) continue;
		// For primitive types, compare only meaningful bytes
		int cmp_size = STACK_STRIDE;
		switch (key_type) {
			case SByte: case Byte: cmp_size = 2; break;  // 1 byte type + 1 byte value
			case Int16: case UInt16: cmp_size = 3; break;  // 1 byte type + 2 bytes value
			case Int32: case UInt32: case Single: cmp_size = 5; break;  // 1 byte type + 4 bytes value
			// ReferenceID and others use full 8 bytes
		}
		if (memcmp(kp, key->bytes, cmp_size) == 0) return i;
	}
	return -1;
}

void builtin_Dictionary_ctor(uchar** reptr) {
	// newobj passes object via builtin_arg0; do not pop 'this' here
	struct object_val* d = expect_builtin_obj(builtin_arg0, BUILTIN_CLSIDX_DICTIONARY, "Dictionary.ctor");
	int storage_id = newarr(LIST_INITIAL_CAPACITY * 2 * STACK_STRIDE, Byte);
	dict_set_storage_ref(d, storage_id);
	dict_set_count(d, 0);
	dict_set_capacity(d, LIST_INITIAL_CAPACITY);
	dict_set_key_type(d, 0);
	dict_set_value_type(d, 0);
}

void builtin_Dictionary_Add(uchar** reptr) {
	stack_value_t val; POP; stack_value_copy(&val, *reptr);
	stack_value_t key; POP; stack_value_copy(&key, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* d = cast_builtin_obj(this_id);
	int count = dict_get_count(d);
	int capacity = dict_get_capacity(d);
	int kt = dict_get_key_type(d); int vt = dict_get_value_type(d);
	if (kt == 0) { dict_set_key_type(d, stack_value_type(&key)); kt = stack_value_type(&key); }
	if (vt == 0) { dict_set_value_type(d, stack_value_type(&val)); vt = stack_value_type(&val); }
	ASSERT_LANG(kt == stack_value_type(&key) && vt == stack_value_type(&val), "Dictionary.Add type mismatch");
	int storage_ref = dict_get_storage_ref(d);
	struct array_val* storage_arr = cast_array(storage_ref);
	uchar* storage = &storage_arr->payload;
	int idx = dict_find_index(d, storage, count, &key);
	ASSERT_LANG(idx < 0, "Dictionary.Add duplicate key");
	if (count >= capacity) {
		int new_capacity = capacity << 1;
		int new_storage_ref = newarr(new_capacity * 2 * STACK_STRIDE, Byte);
		struct array_val* new_arr = cast_array(new_storage_ref);
		if (count > 0) memcpy(&new_arr->payload, storage, count * 2 * STACK_STRIDE);
		dict_set_storage_ref(d, new_storage_ref);
		dict_set_capacity(d, new_capacity);
		storage = &new_arr->payload;
	}
	uchar* key_slot = storage + (count << 1) * STACK_STRIDE;
	stack_value_store(key_slot, &key);
	stack_value_store(key_slot + STACK_STRIDE, &val);
	dict_set_count(d, count + 1);
}

void builtin_Dictionary_get_Item(uchar** reptr) {
	stack_value_t key; POP; stack_value_copy(&key, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* d = cast_builtin_obj(this_id);
	int count = dict_get_count(d);
	struct array_val* storage_arr; uchar* storage = dict_storage_bytes(d, &storage_arr);
	int idx = dict_find_index(d, storage, count, &key);
	ASSERT_RT(idx >= 0, "Dictionary key not found");
	stack_value_t tmp; stack_value_copy(&tmp, storage + (idx * 2 + 1) * STACK_STRIDE);
	push_stack_value(reptr, &tmp);
}

void builtin_Dictionary_set_Item(uchar** reptr) {
	stack_value_t val; POP; stack_value_copy(&val, *reptr);
	stack_value_t key; POP; stack_value_copy(&key, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* d = cast_builtin_obj(this_id);
	int count = dict_get_count(d);
	int capacity = dict_get_capacity(d);
	int kt = dict_get_key_type(d); int vt = dict_get_value_type(d);
	if (kt == 0) { dict_set_key_type(d, stack_value_type(&key)); kt = stack_value_type(&key); }
	if (vt == 0) { dict_set_value_type(d, stack_value_type(&val)); vt = stack_value_type(&val); }
	ASSERT_LANG(kt == stack_value_type(&key) && vt == stack_value_type(&val), "Dictionary.set_Item type mismatch");
	int storage_ref = dict_get_storage_ref(d);
	struct array_val* storage_arr = cast_array(storage_ref);
	uchar* storage = &storage_arr->payload;
	int idx = dict_find_index(d, storage, count, &key);
	if (idx >= 0) {
		stack_value_store(storage + (idx * 2 + 1) * STACK_STRIDE, &val);
		return;
	}
	if (count >= capacity) {
		int new_capacity = capacity << 1;
		int new_storage_ref = newarr(new_capacity * 2 * STACK_STRIDE, Byte);
		struct array_val* new_arr = cast_array(new_storage_ref);
		if (count > 0) memcpy(&new_arr->payload, storage, count * 2 * STACK_STRIDE);
		dict_set_storage_ref(d, new_storage_ref);
		dict_set_capacity(d, new_capacity);
		storage = &new_arr->payload;
	}
	uchar* key_slot = storage + (count << 1) * STACK_STRIDE;
	stack_value_store(key_slot, &key);
	stack_value_store(key_slot + STACK_STRIDE, &val);
	dict_set_count(d, count + 1);
}

void builtin_Dictionary_Remove(uchar** reptr) {
	stack_value_t key; POP; stack_value_copy(&key, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* d = cast_builtin_obj(this_id);
	int count = dict_get_count(d);
	struct array_val* storage_arr; uchar* storage = dict_storage_bytes(d, &storage_arr);
	int idx = dict_find_index(d, storage, count, &key);
	if (idx < 0) { push_bool(reptr, false); return; }
	if (idx != count - 1) {
		memcpy(storage + (idx * 2) * STACK_STRIDE, storage + ((count - 1) * 2) * STACK_STRIDE, 2 * STACK_STRIDE);
	}
	dict_set_count(d, count - 1);
	push_bool(reptr, true);
}

void builtin_Dictionary_ContainsKey(uchar** reptr) {
	stack_value_t key; POP; stack_value_copy(&key, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* d = cast_builtin_obj(this_id);
	int count = dict_get_count(d);
	struct array_val* storage_arr; uchar* storage = dict_storage_bytes(d, &storage_arr);
	push_bool(reptr, dict_find_index(d, storage, count, &key) >= 0);
}

void builtin_Dictionary_get_Count(uchar** reptr) {
	int this_id = pop_reference(reptr);
	push_int(reptr, dict_get_count(cast_builtin_obj(this_id)));
}

// HashSet<T> builtin methods
static int hset_find_index(struct object_val* s, uchar* storage, int count, const stack_value_t* val) {
	uchar val_type = val->bytes[0];
	for (int i = 0; i < count; ++i) {
		uchar* p = storage + i * STACK_STRIDE;
		if (p[0] != val_type) continue;
		int cmp_size = STACK_STRIDE;
		switch (val_type) {
			case SByte: case Byte: cmp_size = 2; break;
			case Int16: case UInt16: cmp_size = 3; break;
			case Int32: case UInt32: case Single: cmp_size = 5; break;
		}
		if (memcmp(p, val->bytes, cmp_size) == 0) return i;
	}
	return -1;
}

void builtin_HashSet_ctor(uchar** reptr) {
    // newobj passes object via builtin_arg0; do not pop 'this' here
    struct object_val* s = expect_builtin_obj(builtin_arg0, BUILTIN_CLSIDX_HASHSET, "HashSet.ctor");
	int storage_id = newarr(LIST_INITIAL_CAPACITY * STACK_STRIDE, Byte);
	hset_set_storage_ref(s, storage_id);
	hset_set_count(s, 0);
	hset_set_capacity(s, LIST_INITIAL_CAPACITY);
	hset_set_element_type(s, 0);
}

void builtin_HashSet_Add(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = hset_get_count(s);
	int capacity = hset_get_capacity(s);
	int elem_type = hset_get_element_type(s);
	uchar val_type = stack_value_type(&value);
	if (elem_type == 0) { hset_set_element_type(s, val_type); elem_type = val_type; }
	ASSERT_RT(elem_type == val_type, "HashSet.Add type mismatch: %d vs %d", elem_type, val_type);
	struct array_val* storage_arr; uchar* storage = hset_storage_bytes(s, &storage_arr);
	if (hset_find_index(s, storage, count, &value) >= 0) { push_bool(reptr, false); return; }
	if (count >= capacity) {
		int new_capacity = capacity * 2;
		int new_storage_ref = newarr(new_capacity * STACK_STRIDE, Byte);
		struct array_val* new_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		memcpy(&new_arr->payload, storage, capacity * STACK_STRIDE);
		hset_set_storage_ref(s, new_storage_ref);
		hset_set_capacity(s, new_capacity);
		storage_arr = (struct array_val*)heap_obj[new_storage_ref].pointer;
		storage = &storage_arr->payload;
	}
	stack_value_store(storage + count * STACK_STRIDE, &value);
	hset_set_count(s, count + 1);
	push_bool(reptr, true);
}

void builtin_HashSet_Remove(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = hset_get_count(s);
	struct array_val* storage_arr; uchar* storage = hset_storage_bytes(s, &storage_arr);
	int idx = hset_find_index(s, storage, count, &value);
	if (idx < 0) { push_bool(reptr, false); return; }
	if (idx != count - 1) memcpy(storage + idx * STACK_STRIDE, storage + (count - 1) * STACK_STRIDE, STACK_STRIDE);
	hset_set_count(s, count - 1);
	push_bool(reptr, true);
}

void builtin_HashSet_Contains(uchar** reptr) {
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	int this_id = pop_reference(reptr);
	struct object_val* s = cast_builtin_obj(this_id);
	int count = hset_get_count(s);
	struct array_val* storage_arr; uchar* storage = hset_storage_bytes(s, &storage_arr);
	int idx = hset_find_index(s, storage, count, &value);
	push_bool(reptr, idx >= 0);
}

void builtin_HashSet_get_Count(uchar** reptr) {
	int this_id = pop_reference(reptr);
	push_int(reptr, hset_get_count(cast_builtin_obj(this_id)));
}


// DefaultInterpolatedStringHandler builtin object helpers
#define DIS_FIELD_LEN 0
#define DIS_FIELD_STORAGE 1

INLINE int dis_obj_get_len(struct object_val* o) { return builtin_field_get_int(o, BUILTIN_CLSIDX_DIS, DIS_FIELD_LEN); }
INLINE void dis_obj_set_len(struct object_val* o, int v) { builtin_field_set_int(o, BUILTIN_CLSIDX_DIS, DIS_FIELD_LEN, v); }
INLINE int dis_obj_get_storage_ref(struct object_val* o) { return builtin_field_get_reference(o, BUILTIN_CLSIDX_DIS, DIS_FIELD_STORAGE); }
INLINE void dis_obj_set_storage_ref(struct object_val* o, int id) { builtin_field_set_reference(o, BUILTIN_CLSIDX_DIS, DIS_FIELD_STORAGE, id); }
INLINE uchar* dis_obj_storage_bytes(struct object_val* o, struct array_val** out_arr) { int r = dis_obj_get_storage_ref(o); if (r == 0) return NULL; struct array_val* a = expect_array(r, Byte, "DIS storage"); if (out_arr) *out_arr = a; return &a->payload; }

INLINE void dis_obj_ensure_capacity(struct object_val* o, int extra)
{
	int len = dis_obj_get_len(o);
	int storage_ref = dis_obj_get_storage_ref(o);
	struct array_val* arr = expect_array(storage_ref, Byte, "DIS ensure capacity");
	int need = len + extra;
	if (need <= arr->len) return;
	int new_cap = arr->len ? arr->len * 2 : 256;
	while (new_cap < need) new_cap *= 2;
	int new_ref = newarr((short)new_cap, Byte);
	struct array_val* new_arr = (struct array_val*)heap_obj[new_ref].pointer;
	memcpy(&new_arr->payload, &arr->payload, len);
	dis_obj_set_storage_ref(o, new_ref);
}

void builtin_DefaultInterpolatedStringHandler_ctor(uchar** reptr)
{
	int formattedCount = pop_int(reptr);
	int literalLength = pop_int(reptr);
	// handler is a value type; initialize the inline struct at the target slot
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler..ctor");
	struct object_val* obj = (struct object_val*)slot;
	// pick an initial capacity based on compiler hints
	int initial_cap = literalLength + formattedCount * 8;
	if (initial_cap < 64) initial_cap = 64;
	int storage_ref = newarr((short)initial_cap, Byte);
	dis_obj_set_storage_ref(obj, storage_ref);
	dis_obj_set_len(obj, 0);
}

void builtin_DefaultInterpolatedStringHandler_AppendLiteral(uchar** reptr)
{
	int str_id = pop_reference(reptr);
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler.AppendLiteral");
	struct object_val* obj = (struct object_val*)slot;
	if (str_id != 0)
	{
		struct string_val* str = (struct string_val*)heap_obj[str_id].pointer;
		ASSERT_LANG(*((uchar*)str) == StringHeader, "AppendLiteral expects string (header=%d)", *((uchar*)str));
		dis_obj_ensure_capacity(obj, str->str_len);
		struct array_val* arr; uchar* bytes = dis_obj_storage_bytes(obj, &arr);
		int len = dis_obj_get_len(obj);
		memcpy(bytes + len, &str->payload, str->str_len);
		dis_obj_set_len(obj, len + str->str_len);
	}
}

void builtin_DefaultInterpolatedStringHandler_AppendFormatted_String(uchar** reptr)
{
	int str_id = pop_reference(reptr);
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler.AppendFormatted(string)");
	struct object_val* obj = (struct object_val*)slot;
	if (str_id != 0)
	{
		struct string_val* str = (struct string_val*)heap_obj[str_id].pointer;
		ASSERT_LANG(*((uchar*)str) == StringHeader, "AppendFormatted(string) expects string (header=%d)", *((uchar*)str));
		dis_obj_ensure_capacity(obj, str->str_len);
		struct array_val* arr; uchar* bytes = dis_obj_storage_bytes(obj, &arr);
		int len = dis_obj_get_len(obj);
		memcpy(bytes + len, &str->payload, str->str_len);
		dis_obj_set_len(obj, len + str->str_len);
	}
}

void builtin_DefaultInterpolatedStringHandler_AppendFormatted_Value(uchar** reptr)
{
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler.AppendFormatted<T>");
	struct object_val* obj = (struct object_val*)slot;
	// Format via existing formatter into a small tmp and append
	char tmp[256];
	char fmt_buf[4] = { '{','0','}',0 };
	uchar* args[1]; args[0] = value.bytes;
	format_string(fmt_buf, tmp, 1, args);
	int add = (int)strlen(tmp);
	dis_obj_ensure_capacity(obj, add);
	struct array_val* arr; uchar* bytes = dis_obj_storage_bytes(obj, &arr);
	int len = dis_obj_get_len(obj);
	memcpy(bytes + len, tmp, add);
	dis_obj_set_len(obj, len + add);
}

void builtin_DefaultInterpolatedStringHandler_AppendFormatted_Value_Format(uchar** reptr)
{
	int format_id = pop_reference(reptr);
	stack_value_t value; POP; stack_value_copy(&value, *reptr);
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler.AppendFormatted<T,String>");
	struct object_val* obj = (struct object_val*)slot;
	struct string_val* format = NULL;
	if (format_id != 0)
	{
		ASSERT_LANG(format_id > 0 && format_id < heap_newobj_id, "AppendFormatted: invalid format reference %d", format_id);
		format = (struct string_val*)heap_obj[format_id].pointer;
		ASSERT_LANG(*((uchar*)format) == StringHeader, "AppendFormatted format arg not string (header %d)", *((uchar*)format));
	}
	char tmp[256];
	// Build format like {0:fmt}
	char fmt_buf[64]; int pos = 0; fmt_buf[pos++] = '{'; fmt_buf[pos++] = '0';
	if (format && format->str_len > 0) { fmt_buf[pos++] = ':'; int cp = format->str_len; if (cp > 30) cp = 30; memcpy(fmt_buf + pos, &format->payload, cp); pos += cp; }
	fmt_buf[pos++] = '}'; fmt_buf[pos] = 0;
	uchar* args[1]; args[0] = value.bytes; format_string(fmt_buf, tmp, 1, args);
	int add = (int)strlen(tmp);
	dis_obj_ensure_capacity(obj, add);
	struct array_val* arr; uchar* bytes = dis_obj_storage_bytes(obj, &arr);
	int len = dis_obj_get_len(obj);
	memcpy(bytes + len, tmp, add);
	dis_obj_set_len(obj, len + add);
}

void builtin_DefaultInterpolatedStringHandler_ToStringAndClear(uchar** reptr)
{
	uchar* slot = pop_value_type_slot(reptr, "DefaultInterpolatedStringHandler.ToStringAndClear");
	struct object_val* obj = (struct object_val*)slot;
	struct array_val* arr; uchar* bytes = dis_obj_storage_bytes(obj, &arr);
	int len = dis_obj_get_len(obj);
	int str_id = newstr((short)len, bytes);
	dis_obj_set_len(obj, 0);
	PUSH_STACK_REFERENCEID(str_id);
}



void builtin_Enumerable_ToList(uchar** reptr) {
    // Treat as identity for arrays: return the same enumerable
    int source_id = pop_reference(reptr);
    PUSH_STACK_REFERENCEID(source_id);
}

void builtin_Enumerable_ToArray(uchar** reptr) {
	int source_id = pop_reference(reptr);
	if (source_id == 0) { PUSH_STACK_REFERENCEID(0); return; }
	uchar* header = heap_obj[source_id].pointer;
	ASSERT_LANG(*header == ArrayHeader, "Enumerable.ToArray expects array source, got %d", *header);
	PUSH_STACK_REFERENCEID(source_id);
}

// Implementation of Enumerable.Where
void builtin_Enumerable_Where(uchar** reptr) {
    // Pop predicate (Func<T,bool>) and source enumerable (array or List)
    int predicate_id = pop_reference(reptr);
    int source_id = pop_reference(reptr);
    if (source_id == 0 || predicate_id == 0) { PUSH_STACK_REFERENCEID(0); return; }

    uchar* source_header = heap_obj[source_id].pointer;
    bool is_list = false;
    struct array_val* source_arr = 0;
    struct object_val* list_obj = 0;
    if (*source_header == ArrayHeader) {
        source_arr = (struct array_val*)source_header;
    } else if (*source_header == ObjectHeader) {
        struct object_val* obj = (struct object_val*)source_header;
        if (obj->clsid == BUILTIN_CLSID(BUILTIN_CLSIDX_LIST)) { is_list = true; list_obj = obj; }
    } else {
		ASSERT_LANG(0, "Enumerable.Where expects array or List source, got %d", *source_header);
    }
    struct object_val* predicate = (struct object_val*)heap_obj[predicate_id].pointer;
    ASSERT_LANG(predicate->clsid == 0xf002 || predicate->clsid == 0xf003, "Where expects a Func<T,bool> delegate, got class ID %d", predicate->clsid);

    int pred_this_id = *(int*)(&predicate->payload + 1);
    int pred_method_id = *(int*)(&predicate->payload + get_val_sz(ReferenceID) + 1);

    int src_len = is_list ? list_get_count(list_obj) : (source_arr ? source_arr->len : 0);
    uchar src_type = is_list ? (uchar)list_get_element_type(list_obj) : (source_arr ? source_arr->typeid : 0);
    // Temporary result array with max size
    int tmp_arr_id = newarr(src_len, src_type);
    struct array_val* tmp_arr = (struct array_val*)heap_obj[tmp_arr_id].pointer;
    int out_idx = 0;
    int elem_sz = get_type_sz(src_type);

    for (int i = 0; i < src_len; i++) {
        // Prepare stack for delegate invoke: [.., delegate_ref, arg]
        PUSH_STACK_REFERENCEID(predicate_id);
        if (!is_list) {
            if (src_type == ReferenceID) {
                int element_id = *(int*)(&source_arr->payload + i * elem_sz);
                PUSH_STACK_REFERENCEID(element_id);
            } else {
                **reptr = src_type;
                memcpy(*reptr + 1, &source_arr->payload + i * elem_sz, elem_sz);
                *reptr += STACK_STRIDE;
            }
        } else {
            struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
            push_stack_value(reptr, (stack_value_t*)(storage + i * STACK_STRIDE));
        }
        // Invoke Func<T,bool>
        delegate_ivk(reptr, 0xf003, 1);
        // Expect boolean-like result: accept Boolean or integral 0/1
        POP;
        uchar rtype = **reptr;
        bool keep = false;
        if (rtype == 0) { // Boolean
            keep = *(*reptr + 1) ? true : false;
        } else if (rtype == Int32 || rtype == UInt32 || rtype == Int16 || rtype == UInt16 || rtype == SByte || rtype == Byte) {
            keep = (*(int*)(*reptr + 1)) != 0;
        } else {
            ASSERT_LANG(0, "Where predicate must return Boolean, got %d", rtype);
        }
        if (keep) {
            if (src_type == ReferenceID) {
                if (!is_list) {
                    int element_id = *(int*)(&source_arr->payload + i * elem_sz);
                    *(int*)(&tmp_arr->payload + out_idx * elem_sz) = element_id;
                } else {
                    struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
                    int element_id = *(int*)(storage + i * STACK_STRIDE + 1);
                    *(int*)(&tmp_arr->payload + out_idx * elem_sz) = element_id;
                }
            } else {
                if (!is_list) memcpy(&tmp_arr->payload + out_idx * elem_sz, &source_arr->payload + i * elem_sz, elem_sz);
                else {
                    struct array_val* storage_arr; uchar* storage = list_storage_bytes(list_obj, &storage_arr);
                    memcpy(&tmp_arr->payload + out_idx * elem_sz, storage + i * STACK_STRIDE + 1, elem_sz);
                }
            }
            out_idx++;
        }
    }

    // If no elements were filtered out, we can return the original array as-is;
    // but for List sources we must still materialize an array for ToArray downstream.
    if (!is_list && out_idx == src_len) { PUSH_STACK_REFERENCEID(source_id); return; }
    int result_arr_id = newarr(out_idx, src_type);
    struct array_val* result_arr = (struct array_val*)heap_obj[result_arr_id].pointer;
    memcpy(&result_arr->payload, &tmp_arr->payload, out_idx * elem_sz);
    PUSH_STACK_REFERENCEID(result_arr_id);
}

// Implementation of Enumerable.Sum for Int32 sequences
void builtin_Enumerable_Sum(uchar** reptr) {
    int source_id = pop_reference(reptr);
    if (source_id == 0) { PUSH_STACK_INT(0); return; }
    uchar* header = heap_obj[source_id].pointer;
    ASSERT_LANG(*header == ArrayHeader, "Sum expects array source, got %d", *header);
    struct array_val* arr = (struct array_val*)header;
    ASSERT_LANG(arr->typeid == Int32, "Sum only supports Int32 sequences, got type %d", arr->typeid);
    int sum = 0; int sz = get_type_sz(Int32);
    for (int i = 0; i < arr->len; ++i) sum += *(int*)(&arr->payload + i * sz);
    PUSH_STACK_INT(sum);
}

// Implementation of Enumerable.Max for Int32 sequences
void builtin_Enumerable_Max(uchar** reptr) {
    int source_id = pop_reference(reptr);
    if (source_id == 0) { PUSH_STACK_INT(0); return; }
    uchar* header = heap_obj[source_id].pointer;
    ASSERT_LANG(*header == ArrayHeader, "Max expects array source, got %d", *header);
    struct array_val* arr = (struct array_val*)header;
    ASSERT_RT(arr->len > 0, "Sequence contains no elements");
    ASSERT_LANG(arr->typeid == Int32, "Max only supports Int32 sequences, got type %d", arr->typeid);
    int sz = get_type_sz(Int32); int m = *(int*)(&arr->payload + 0 * sz);
    for (int i = 1; i < arr->len; ++i) { int v = *(int*)(&arr->payload + i * sz); if (v > m) m = v; }
    PUSH_STACK_INT(m);
}

// Implementation of Enumerable.Min for Int32 sequences
void builtin_Enumerable_Min(uchar** reptr) {
    int source_id = pop_reference(reptr);
    if (source_id == 0) { PUSH_STACK_INT(0); return; }
    uchar* header = heap_obj[source_id].pointer;
    ASSERT_LANG(*header == ArrayHeader, "Min expects array source, got %d", *header);
    struct array_val* arr = (struct array_val*)header;
    ASSERT_RT(arr->len > 0, "Sequence contains no elements");
    ASSERT_LANG(arr->typeid == Int32, "Min only supports Int32 sequences, got type %d", arr->typeid);
    int sz = get_type_sz(Int32); int m = *(int*)(&arr->payload + 0 * sz);
    for (int i = 1; i < arr->len; ++i) { int v = *(int*)(&arr->payload + i * sz); if (v < m) m = v; }
    PUSH_STACK_INT(m);
}

// Implementation of Enumerable.DefaultIfEmpty for Int32 sequences
void builtin_Enumerable_DefaultIfEmpty(uchar** reptr) {
    // Pop default value (T) then source enumerable
    POP; // value
    uchar def_type = **reptr; int def_raw = *(int*)(*reptr + 1);
    int source_id = pop_reference(reptr);
    if (source_id == 0) {
        int rid = newarr(1, def_type);
        struct array_val* arr = (struct array_val*)heap_obj[rid].pointer;
        memcpy(&arr->payload, &def_raw, get_type_sz(def_type));
        PUSH_STACK_REFERENCEID(rid);
        return;
    }
    uchar* header = heap_obj[source_id].pointer;
    ASSERT_LANG(*header == ArrayHeader, "DefaultIfEmpty expects array source, got %d", *header);
    struct array_val* arr = (struct array_val*)header;
    if (arr->len > 0) { PUSH_STACK_REFERENCEID(source_id); return; }
    int rid = newarr(1, def_type);
    struct array_val* out_arr = (struct array_val*)heap_obj[rid].pointer;
    memcpy(&out_arr->payload, &def_raw, get_type_sz(def_type));
    PUSH_STACK_REFERENCEID(rid);
}

// Helper function to set up the built-in method table
void setup_builtin_methods() {
	bn = 0;  // Reset counter

	// Core built-ins
	builtin_methods[bn++] = builtin_Object_ctor; //0
	builtin_methods[bn++] = builtin_Math_Abs_Decimal; //1
	builtin_methods[bn++] = builtin_Math_Abs_Double; //2
	builtin_methods[bn++] = builtin_Math_Abs_Int16; //3
	builtin_methods[bn++] = builtin_Math_Abs_Int32; //4
	builtin_methods[bn++] = builtin_Math_Abs_Int64; //5
	builtin_methods[bn++] = builtin_Math_Abs_SByte; //6
	builtin_methods[bn++] = builtin_Math_Abs_Single; //7
	builtin_methods[bn++] = builtin_Math_Acos; //8
	builtin_methods[bn++] = builtin_Math_Acosh; //9
	builtin_methods[bn++] = builtin_Math_Asin; //10
	builtin_methods[bn++] = builtin_Math_Asinh; //11
	builtin_methods[bn++] = builtin_Math_Atan; //12
	builtin_methods[bn++] = builtin_Math_Atan2; //13
	builtin_methods[bn++] = builtin_Math_Atanh; //14
	builtin_methods[bn++] = builtin_Math_Ceiling; //15
	builtin_methods[bn++] = builtin_Math_Clamp_Double; //16
	builtin_methods[bn++] = builtin_Math_Clamp_Int16; //17
	builtin_methods[bn++] = builtin_Math_Clamp_Int32; //18
	builtin_methods[bn++] = builtin_Math_Clamp_Int64; //19
	builtin_methods[bn++] = builtin_Math_Clamp_SByte; //20
	builtin_methods[bn++] = builtin_Math_Clamp_Single; //21
	builtin_methods[bn++] = builtin_Math_Cos; //22
	builtin_methods[bn++] = builtin_Math_Cosh; //23
	builtin_methods[bn++] = builtin_Math_Exp; //24
	builtin_methods[bn++] = builtin_Math_Floor; //25
	builtin_methods[bn++] = builtin_Math_Log; //26
	builtin_methods[bn++] = builtin_Math_Log_Base; //27
	builtin_methods[bn++] = builtin_Math_Log10; //28
	builtin_methods[bn++] = builtin_Math_Log2; //29
	builtin_methods[bn++] = builtin_Math_Max_Double; //30
	builtin_methods[bn++] = builtin_Math_Max_Int16; //31
	builtin_methods[bn++] = builtin_Math_Max_Int32; //32
	builtin_methods[bn++] = builtin_Math_Max_Int64; //33
	builtin_methods[bn++] = builtin_Math_Max_SByte; //34
	builtin_methods[bn++] = builtin_Math_Max_Single; //35
	builtin_methods[bn++] = builtin_Math_Min_Decimal; //36
	builtin_methods[bn++] = builtin_Math_Min_Double; //37
	builtin_methods[bn++] = builtin_Math_Min_Int16; //38
	builtin_methods[bn++] = builtin_Math_Min_Int32; //39
	builtin_methods[bn++] = builtin_Math_Min_Int64; //40
	builtin_methods[bn++] = builtin_Math_Min_SByte; //41
	builtin_methods[bn++] = builtin_Math_Min_Single; //42
	builtin_methods[bn++] = builtin_Math_Pow; //43
	builtin_methods[bn++] = builtin_Math_Round; //44
	builtin_methods[bn++] = builtin_Math_Sign_Double; //45
	builtin_methods[bn++] = builtin_Math_Sign_Int16; //46
	builtin_methods[bn++] = builtin_Math_Sign_Int32; //47
	builtin_methods[bn++] = builtin_Math_Sign_Int64; //48
	builtin_methods[bn++] = builtin_Math_Sign_SByte; //49
	builtin_methods[bn++] = builtin_Math_Sign_Single; //50
	builtin_methods[bn++] = builtin_Math_Sin; //51
	builtin_methods[bn++] = builtin_Math_Sinh; //52
	builtin_methods[bn++] = builtin_Math_Sqrt; //53
	builtin_methods[bn++] = builtin_Math_Tan; //54
	builtin_methods[bn++] = builtin_Math_Tanh; //55

	builtin_methods[bn++] = builtin_String_Format_1; //56
	builtin_methods[bn++] = builtin_String_Format_2; //57
	builtin_methods[bn++] = builtin_String_Format_3; //58
	builtin_methods[bn++] = builtin_String_Format_Array; //59
	builtin_methods[bn++] = builtin_String_Concat_2; //60
	builtin_methods[bn++] = builtin_String_Concat_3; //61
	builtin_methods[bn++] = builtin_String_Concat_4; //62
	builtin_methods[bn++] = builtin_String_Substring_2; //63
	builtin_methods[bn++] = builtin_String_get_Length; //64

	builtin_methods[bn++] = builtin_RunOnMCU_ReadEvent; //65
	builtin_methods[bn++] = builtin_RunOnMCU_ReadSnapshot; //66
	builtin_methods[bn++] = builtin_RunOnMCU_ReadStream; //67
	builtin_methods[bn++] = builtin_RunOnMCU_WriteEvent; //68
	builtin_methods[bn++] = builtin_RunOnMCU_WriteSnapshot; //69
	builtin_methods[bn++] = builtin_RunOnMCU_WriteStream; //70

	builtin_methods[bn++] = builtin_RunOnMCU_GetMicrosFromStart; //71
	builtin_methods[bn++] = builtin_RunOnMCU_GetMillisFromStart; //72
	builtin_methods[bn++] = builtin_RunOnMCU_GetSecondsFromStart; //73

	builtin_methods[bn++] = builtin_ValueTuple2_ctor; //74
	builtin_methods[bn++] = builtin_ValueTuple3_ctor; //75
	builtin_methods[bn++] = builtin_ValueTuple4_ctor; //76

	builtin_methods[bn++] = builtin_RuntimeHelpers_InitializeArray; //77

	builtin_methods[bn++] = builtin_Boolean_ToString; //78
	builtin_methods[bn++] = builtin_Byte_ToString; //79
	builtin_methods[bn++] = builtin_Char_ToString; //80
	builtin_methods[bn++] = builtin_Int16_ToString; //81
	builtin_methods[bn++] = builtin_Int32_ToString; //82
	builtin_methods[bn++] = builtin_Single_ToString; //83
	builtin_methods[bn++] = builtin_UInt16_ToString; //84
	builtin_methods[bn++] = builtin_UInt32_ToString; //85

	builtin_methods[bn++] = builtin_Action_ctor; //86
	builtin_methods[bn++] = builtin_Action_Invoke; //87
	builtin_methods[bn++] = builtin_Action1_ctor; //88
	builtin_methods[bn++] = builtin_Action1_Invoke; //89
	builtin_methods[bn++] = builtin_Action2_ctor; //90
	builtin_methods[bn++] = builtin_Action2_Invoke; //91
	builtin_methods[bn++] = builtin_Action3_ctor; //92
	builtin_methods[bn++] = builtin_Action3_Invoke; //93
	builtin_methods[bn++] = builtin_Action4_ctor; //94
	builtin_methods[bn++] = builtin_Action4_Invoke; //95
	builtin_methods[bn++] = builtin_Action5_ctor; //96
	builtin_methods[bn++] = builtin_Action5_Invoke; //97
	builtin_methods[bn++] = builtin_Func1_ctor; //98
	builtin_methods[bn++] = builtin_Func1_Invoke; //99
	builtin_methods[bn++] = builtin_Func2_ctor; //100
	builtin_methods[bn++] = builtin_Func2_Invoke; //101
	builtin_methods[bn++] = builtin_Func3_ctor; //102
	builtin_methods[bn++] = builtin_Func3_Invoke; //103
	builtin_methods[bn++] = builtin_Func4_ctor; //104
	builtin_methods[bn++] = builtin_Func4_Invoke; //105
	builtin_methods[bn++] = builtin_Func5_ctor; //106
	builtin_methods[bn++] = builtin_Func5_Invoke; //107
	builtin_methods[bn++] = builtin_Func6_ctor; //108
	builtin_methods[bn++] = builtin_Func6_Invoke; //109
	builtin_methods[bn++] = builtin_Console_WriteLine; //110

	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Boolean; //111
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Char; //112
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Int16; //113
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Int32; //114
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Single; //115
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_UInt16; //116
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_UInt32; //117
	builtin_methods[bn++] = builtin_BitConverter_ToBoolean; //118
	builtin_methods[bn++] = builtin_BitConverter_ToChar; //119
	builtin_methods[bn++] = builtin_BitConverter_ToInt16; //120
	builtin_methods[bn++] = builtin_BitConverter_ToInt32; //121
	builtin_methods[bn++] = builtin_BitConverter_ToSingle; //122
	builtin_methods[bn++] = builtin_BitConverter_ToUInt16; //123
	builtin_methods[bn++] = builtin_BitConverter_ToUInt32; //124

	// Add our new methods
	builtin_methods[bn++] = builtin_String_Join_IEnumerable; //125
	builtin_methods[bn++] = builtin_String_Join_ObjectArray; //126
	builtin_methods[bn++] = builtin_Enumerable_Select; //127

	// List<T> support
	builtin_methods[bn++] = builtin_List_ctor; //128
	builtin_methods[bn++] = builtin_List_Add; //129
	builtin_methods[bn++] = builtin_List_get_Count; //130
	builtin_methods[bn++] = builtin_List_get_Item; //131
	builtin_methods[bn++] = builtin_List_set_Item; //132
	builtin_methods[bn++] = builtin_List_RemoveAt; //133
	builtin_methods[bn++] = builtin_List_Clear; //134
	builtin_methods[bn++] = builtin_List_Contains; //135
	builtin_methods[bn++] = builtin_List_IndexOf; //136
	builtin_methods[bn++] = builtin_List_InsertRange; //137
	builtin_methods[bn++] = builtin_Enumerable_ToList; //138

	// batch 2 builtins:
	builtin_methods[bn++] = builtin_Enumerable_Where; //139
	builtin_methods[bn++] = builtin_Enumerable_Sum; //140
	builtin_methods[bn++] = builtin_Enumerable_Max; //141
	builtin_methods[bn++] = builtin_Enumerable_Min; //142
	builtin_methods[bn++] = builtin_Enumerable_DefaultIfEmpty; //143
	builtin_methods[bn++] = builtin_Enumerable_ToArray; //144

	// Queue<T>
	builtin_methods[bn++] = builtin_Queue_ctor; //145
	builtin_methods[bn++] = builtin_Queue_Enqueue; //146
	builtin_methods[bn++] = builtin_Queue_Dequeue; //147
	builtin_methods[bn++] = builtin_Queue_Peek; //148
	builtin_methods[bn++] = builtin_Queue_get_Count; //149

	// Stack<T>
	builtin_methods[bn++] = builtin_Stack_ctor; //150
	builtin_methods[bn++] = builtin_Stack_Push; //151
	builtin_methods[bn++] = builtin_Stack_Pop; //152
	builtin_methods[bn++] = builtin_Stack_Peek; //153
	builtin_methods[bn++] = builtin_Stack_get_Count; //154

	// Dictionary<TKey,TValue>
	builtin_methods[bn++] = builtin_Dictionary_ctor; //155
	builtin_methods[bn++] = builtin_Dictionary_Add; //156
	builtin_methods[bn++] = builtin_Dictionary_get_Item; //157
	builtin_methods[bn++] = builtin_Dictionary_set_Item; //158
	builtin_methods[bn++] = builtin_Dictionary_Remove; //159
	builtin_methods[bn++] = builtin_Dictionary_ContainsKey; //160
	builtin_methods[bn++] = builtin_Dictionary_get_Count; //161

	// HashSet<T>
	builtin_methods[bn++] = builtin_HashSet_ctor; //162
	builtin_methods[bn++] = builtin_HashSet_Add; //163
	builtin_methods[bn++] = builtin_HashSet_Remove; //164
	builtin_methods[bn++] = builtin_HashSet_Contains; //165
	builtin_methods[bn++] = builtin_HashSet_get_Count; //166


	// Compiler: String Interpolation.
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_ctor; //167
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_AppendLiteral; //168
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_AppendFormatted_String; //169
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_AppendFormatted_Value; //170
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_AppendFormatted_Value_Format; //171
	builtin_methods[bn++] = builtin_DefaultInterpolatedStringHandler_ToStringAndClear; //172

	DBG("System builtin methods n=%d", bn);
	add_additional_builtins();

	ASSERT_RT(bn < NUM_BUILTIN_METHODS, "Too many built-in methods (%d >= %d)! Increase NUM_BUILTIN_METHODS", bn, NUM_BUILTIN_METHODS);


}

/// ##########################################################################################
///
///
///     ▄▄▄▄   ▄▄▄▄▄▄ ▄▄▄▄▄  ▄    ▄   ▄▄▄
///     █   ▀▄ █      █    █ █    █ ▄▀   ▀
///     █    █ █▄▄▄▄▄ █▄▄▄▄▀ █    █ █   ▄▄
///     █    █ █      █    █ █    █ █    █
///     █▄▄▄▀  █▄▄▄▄▄ █▄▄▄▄▀ ▀▄▄▄▄▀  ▀▄▄▄▀
///     
///                     
/// ##########################################################################################
///
///	WINDOWS DEBUG:
///
///
#ifdef _DEBUG


void write_snapshot(uchar* buffer, int size)
{
	int width = 128;
	int height = 64;
	for (int y = 0; y < 64; ++y)
	{
		for (int x = 0; x < 128; ++x)
		{
			char bit = (buffer[(y / 8) * width + x] & (1 << (y % 8))) != 0;
			if (bit) printf("\u2588");
			else printf(" ");
		}
		printf("\n");
	}
} // size is equal to "vm_put_snapshot_buffer"

#ifndef IS_MCU
// Dummy functions on PC
void write_stream(int streamID, uchar* buffer, int size) {} // called to write bytes into serial.
void write_event(int portID, int eventID, uchar* buffer, int size) {} // called to write bytes into CAN/modbus similar ports.


typedef void(*NotifyErr)(int il_offset, unsigned char* error_msg, int length);
NotifyErr err_cb = 0;
__declspec(dllexport) void set_error_report_cb(NotifyErr cb)
{
	err_cb = cb;
}

void print_stacktrace(void) {
	void* stack[64];
	HANDLE proc = GetCurrentProcess();
	SymSetOptions(SYMOPT_LOAD_LINES);       // enable line lookup
	SymInitialize(proc, NULL, TRUE);

	USHORT n = CaptureStackBackTrace(0, 64, stack, NULL);
	SYMBOL_INFO* sym = calloc(1, sizeof(SYMBOL_INFO) + 256);
	sym->MaxNameLen = 255;
	sym->SizeOfStruct = sizeof(SYMBOL_INFO);

	IMAGEHLP_LINE64 line;
	DWORD disp;
	printf("== STACKTRACE of MCU_RUNTIME ===\n");
	for (USHORT i = 0; i < n; ++i) {
		DWORD64 addr = (DWORD64)stack[i];
		if (SymFromAddr(proc, addr, 0, sym)) {
			printf("%02u: %s ", i, sym->Name);
			if (SymGetLineFromAddr64(proc, addr, &disp, &line))
				printf(" (%s:%lu)", line.FileName, line.LineNumber);
			printf("\n");
		}
	}
	free(sym);
}

void report_error(int il_offset, uchar* error_str) { 
	err_cb(il_offset, error_str, strlen(error_str));
	print_stacktrace();
	exit(2);
}
void print_line(uchar* error_str) { printf("%s\n", error_str); }; // should upload text info.

inline void enter_critical() {};
inline void leave_critical() {};

inline int get_cyclic_millis() {}
inline int get_cyclic_micros() {}
inline int get_cyclic_seconds() {}

#endif

void print_hex(const unsigned char* buffer, size_t size) {
	for (size_t i = 0; i < size; ++i) {
		printf("%02x ", buffer[i]);
	}
	printf("\n");
}

typedef void(*NotifyLower)(unsigned char* changedStates, int length);
NotifyLower stateCallback = 0;

__declspec(dllexport) void set_lowerio_cb(NotifyLower cb)
{
	stateCallback = cb;
}
__declspec(dllexport) void put_upper(uchar* buf, int len)
{
	vm_put_upper_memory(buf, len);
}
__declspec(dllexport) void test(uchar* bin, int len)
{
	printf("====START TEST===:\r\n");
	vm_set_program(bin, len);

	for (int i = 0; i < 100000; ++i)
	{ 
		char buffer[38];
		vm_put_snapshot_buffer(buffer, 38);
		char bufferevent[8] = { i & 0xff,1,2,3,5,8,13,21 };
		vm_put_event_buffer(0, 0x80, bufferevent, 8);
		vm_run(i);

		uchar* mem = vm_get_lower_memory();
		int len = vm_get_lower_memory_size();
		//printf(">>> %d finished, mem swap...\n", i);
		//print_hex(vm_get_lower_memory(), vm_get_lower_memory_size());

		if (stateCallback != 0)
			stateCallback(mem, len);

		// system("pause");
	}
}
#endif

#pragma pack(pop)
