#ifndef IS_MCU
#include "mcu_runtime.h"
#else
#include "appl/vm.h"
#include "util/console.h"
#include "appl/states.h"
#undef _DEBUG
#endif

// allow unsafe strings...
#ifdef _MSC_VER
#define _CRT_SECURE_NO_WARNINGS
#define _CRT_NONSTDC_NO_DEPRECATE
#endif

// the required library
#include <string.h>   // memcpy/memset
#include <stdio.h>    // sprintf
#include <stdlib.h>   // atoi/iota
#include <math.h>     // all
#include <stdint.h>

//hint: all structs are 1 bytes aligned.
#pragma pack(push, 1)


#ifdef _DEBUG
#define INLINE static inline

// #define _VERBOSE

#ifdef _VERBOSE
#define DBG printf
#define INFO printf
#else
#define DBG ;
#define INFO ;
#endif
// printf
#define WARN printf

void debugger_break()
{
	system("pause");
}

#define DIEIF(expr) if (expr)
#define DOOM(...) {printf(__VA_ARGS__); debugger_break();}

#else

// release mode functions.

#define INLINE __attribute__((always_inline)) static inline
VAL_OUT(ptr);
#define DBG(...) ;
#define INFO(...) ;
#define WARN(...) ;

char err_tmp[256];
#define DOOM(...) { int sz = snprintf(err_tmp,sizeof(err_tmp),__VA_ARGS__); report_error(__FILE__, __LINE__, err_tmp, sz);}
#define DIEIF(expr) if (0)

#endif

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
int builtin_arg0; //this pointer.


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
		DOOM("invalid typeid %d\n", typeid);
	}
}
uchar get_val_sz(uchar typeid)
{
	return get_type_sz(typeid) + 1;
}

// builtin class fields, each fields...
uchar builtin_cls_delegate[] = { 2, ReferenceID, Int32 };

uchar* builtin_cls[] = {
	builtin_cls_delegate, //Action
	builtin_cls_delegate, //Action1
	builtin_cls_delegate, //Func1
	builtin_cls_delegate, //Func2
	builtin_cls_delegate, //Action2
	builtin_cls_delegate, //Action3
	builtin_cls_delegate, //Action4
	builtin_cls_delegate, //Action5
	builtin_cls_delegate, //Func3
	builtin_cls_delegate, //Func4
	builtin_cls_delegate, //Func5
};


struct object_val* monitor_obj = 0;
// use heap_newobj_id-1 to get obj_id.
int newobj(int clsid)
{
	int reference_id = heap_newobj_id;
	heap_newobj_id++;
	uchar* tail = reference_id == 1 ? heap_tail : heap_obj[reference_id - 1].pointer;
	int mysz = instanceable_class_layout_ptr[clsid].tot_size + ObjectHeaderSize;
	struct object_val* my_ptr = tail - mysz;
	if (new_stack_depth > 0 && (uchar*)my_ptr < stack_ptr[new_stack_depth - 1]->evaluation_pointer)
		DOOM("Not enough space allocating %d bytes for obj(%d), heap available=%d, ttl=%d", mysz, clsid, (int)(tail - stack_ptr[new_stack_depth - 1]->evaluation_pointer), (int)(tail - mem0));
	heap_obj[reference_id] = (struct heap_obj_slot){ .pointer = my_ptr, };
	// initialize:
	my_ptr->header = ObjectHeader;
	my_ptr->clsid = clsid;

	// set to zero as default value.
	memset(&my_ptr->payload, 0, mysz - ObjectHeaderSize);

	struct per_field* my_layout = instanceable_class_per_layout_ptr + instanceable_class_layout_ptr[clsid].layout_offset;

	for (int i = 0; i < instanceable_class_layout_ptr[clsid].n_of_fields; ++i) {
		(&my_ptr->payload)[my_layout[i].offset] = my_layout[i].typeid;
		if (my_layout[i].aux != -1 && my_layout[i].typeid == ReferenceID)
		{
			*((int*)(&my_ptr->payload + my_layout[i].offset + 1)) = newobj(my_layout[i].aux);
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
		DOOM("Not enough space allocating %d bytes for str[%d], heap available=%d, ttl=%d", mysz, len, (int)(tail - stack_ptr[new_stack_depth - 1]->evaluation_pointer), (int)(tail - mem0));
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
		DOOM("Not enough space allocating %dB for arr[%d](%d), heap available=%d, ttl=%d", mysz, len, type_id, (int)(tail - stack_ptr[new_stack_depth - 1]->evaluation_pointer), (int)(tail - mem0));
	heap_obj[reference_id] = (struct heap_obj_slot){ .pointer = my_ptr, };

	// initialize:
	my_ptr->header = ArrayHeader;
	my_ptr->typeid = type_id;
	my_ptr->len = len;

	// set to zero as default value.
	memset(&my_ptr->payload, 0, mysz - ArrayHeaderSize);

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
	uchar paramCnt = *ptr + 1;
	struct { short clsid; short methodid; } *vm_s = ptr + 2;
	for (int i = 0; i < ncls; ++i, vm_s++)
		if (vm_s->clsid == cls_id) return vm_s->methodid;
	DOOM("Cannot find vmethod %d for type %d\n", vmethod_id, cls_id);
}
void setup_builtin_methods();

int iterations = 0;

void mark_object(int obj_id);

int vm_set_program(uchar* vm_memory, int vm_memory_size)
{
	setup_builtin_methods();

	uchar* ptr = mem0 = vm_memory;
	auto interval = ReadInt;
	entry_method_id = ReadInt;
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
	newobj(ladderlogic_this_clsid);

	// parse statics desc to get stack0 ptr.
	parse_statics();

	iterations = 0;

	processing_buf->offset = writing_buf->offset = 0;
	processing_buf->N_slots = writing_buf->N_slots = 0;

	return interval;
}



#define HEAP_WRITE_INT(val) *heap=Int32; As(heap+1, int)=val; heap+=get_val_sz(Int32);
#define HEAP_WRITE_REFERENCEID(val) *heap=ReferenceID; As(heap+1, int)=val; heap+=get_val_sz(ReferenceID);

// PUSH 
#define PUSH_STACK_INT8(val) *eptr = SByte; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT8(val) *eptr = Byte; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT16(val) *eptr = Int16; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT16(val) *eptr = UInt16; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT(val) *eptr = Int32; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT(val) *eptr = UInt32; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;

// now our stack has eptr+1 8B aligned.
#define PUSH_STACK_FLOAT_D(val) *eptr = Single; As(eptr + 1, float) = (val); eptr+=STACK_STRIDE;
#define PUSH_STACK_FLOAT_M(val) { int ival = *(int*)&(val); *eptr = Single; As(eptr + 1, int) = *(int*)&(ival); eptr+=STACK_STRIDE; }

#define PUSH_STACK_METHODHANDLER(val) *eptr = MethodPointer; As(eptr + 1, struct method_pointer) = (val); eptr+=STACK_STRIDE;

// not on reference id: it's heap object ID, not address!!!
#define PUSH_STACK_REFERENCEID(val) *eptr = ReferenceID; As(eptr + 1, int) = val; eptr+=STACK_STRIDE;

// address is starting at mem0;
#define PUSH_STACK_ADDRESS(val, typeid) *eptr = Address; As(eptr + 1, int) = (int)(val-mem0); *(eptr+5)=typeid; eptr+=STACK_STRIDE;
// typed addr is always on stack.
#define TypedAddrAsValPtr(what) (mem0+As((what)+1, int))
#define TypedAddrGetType(what) (*(((uchar*)what)+5))

// push stack indirect use mem0+ address.
// simply copy 2 ints.
#define PUSH_STACK_INDIRECT(addr) {*(int*)eptr=*(int*)addr; ((int*)eptr)[1]=((int*)addr)[1]; eptr+=8;}

#define POP {eptr-=STACK_STRIDE;\
    DIEIF (eptr<my_stack->evaluation_st_ptr) DOOM("POP exceeds range!"); }

void vm_push_stack(int method_id, int new_obj_id, uchar** reptr);

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
		case 0: dst[1] = src[1]; INFO("b->i1\n"); return;
		case 1:case 2:dst[1] = src[1]; return;
		case 3:case 4:case 5:case 6:case 7: dst[1] = src[1]; INFO("i2+ ->i1\n"); return;
		default:
			DOOM("invalid i1 value copy from type_%d", *src);
		}
	case 3:
	case 4:
	case 5:
		switch (*src)
		{
		case 1: *(int16_t*)(dst + 1) = src[1];
			INFO("u1->iu2\n");
			return;
		case 2: *(int16_t*)(dst + 1) = ((int8_t*)src)[1];
			INFO("i1->iu2\n");
			return;
		case 3:
		case 4:
		case 5:
		case 6:
		case 7: *(int16_t*)(dst + 1) = *(int16_t*)(src + 1);
			return;
		}
		DOOM("invalid i2 value copy from type_%d", *src);

	case 6:
	case 7:
		switch (*src)
		{
		case 1:
			*(int32_t*)(dst + 1) = src[1];
			INFO("u1->iu4\n");
		case 2:
			*(int32_t*)(dst + 1) = ((int8_t*)src)[1];
			INFO("i1->iu4\n");
			return;
		case 3:
		case 4: *(int32_t*)(dst + 1) = *(int16_t*)(src + 1);
			INFO("i2->iu4\n");
		case 5: *(int32_t*)(dst + 1) = *(uint16_t*)(src + 1);
			INFO("u2->iu4\n");
			return;
		case 6:
		case 7: *(int32_t*)(dst + 1) = *(int32_t*)(src + 1);
			return;
		}
		DOOM("invalid i4 value copy from type_%d", *src);

	case 8:
		if (*src == 8)
		{
			// just use int copy.
			*(int*)(dst + 1) = *(int*)(src + 1);
			return;
		}
		else
		{
			DOOM("invalid r4 value copy from type_%d", *src);
		}
	case ReferenceID:
		switch (*src)
		{
		case ReferenceID:
			*(int32_t*)(dst + 1) = *(int32_t*)(src + 1);
			return;
		case JumpAddress:
			INFO("case of copy from JMP to REFID\n");
			// store a struct to heap object. ok, we create copy the object
			struct object_val* obj_src = TypedAddrAsValPtr(src);
			int refid = newobj(obj_src->clsid);
			struct object_val* obj_dst = heap_obj[refid].pointer;
			memcpy(obj_dst, obj_src, instanceable_class_layout_ptr[obj_src->clsid].tot_size + ObjectHeaderSize);
			As(dst + 1, int) = refid;
			return;
		}
		DOOM("invalid ref value copy from type_%d", *src);
	case JumpAddress:
		struct object_val* obj_dst = TypedAddrAsValPtr(dst);
		struct object_val* obj_src = 0;
		switch (*src)
		{
		case ReferenceID:
			INFO("case of copy from RefID to JMP\n");
			int ref_id = As(src + 1, int);
			if (ref_id == 0) DOOM("copy error: copy nullptr to struct?")
				obj_src = heap_obj[ref_id].pointer;
			break;
		case JumpAddress:
			obj_src = TypedAddrAsValPtr(src);
			break;
		default:
			DOOM("invalid struct ja value copy from type_%d", *src);
		}
		memcpy(obj_dst, obj_src, instanceable_class_layout_ptr[obj_src->clsid].tot_size + ObjectHeaderSize);
		return;
	case Address:
		//just copy.
		*(int*)(dst + 1) = *(int*)(src + 1);
		*(dst + 5) = *(src + 5);
		return;
	default:
		DOOM("invalid copy dst type_%d", *dst);
	}
}


void vm_push_stack(int method_id, int new_obj_id, uchar** reptr)
{
	if (method_id >= methods_N)
		DOOM("Bad method id_%d>%d\n", method_id, methods_N);

	int my_stack_depth = new_stack_depth;
	new_stack_depth += 1;
	struct stack_frame_header* my_stack = my_stack_depth == 0 ? stack0 : stack_ptr[my_stack_depth - 1]->evaluation_pointer;
	stack_ptr[my_stack_depth] = my_stack;
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
			DOOM("Entry Method must be 'void Operation(int i);\n");
		}
		//reference_id = 1. if it's the root method, `this` is always obj1.
		*sptr = ReferenceID; As(sptr + 1, int) = 1; sptr += 1 + get_type_sz(ReferenceID);
		*sptr = Int32; As(sptr + 1, int) = iterations; sptr += 1 + get_type_sz(Int32);
	}
	else {
		// validate previous stack frame.
		struct stack_frame_header* my_stack = stack_ptr[new_stack_depth - 1];
		uchar* eptr = my_stack; // previous stack_ep.

		for (int i = n_args - 1; i >= (new_obj_id > 0 ? 1 : 0); --i)
		{
			uchar typeid = ptr[i * 3]; // actually it's reverse order.
			POP;
			// 20250522: we do type check on actual copy.
			// if (typeid == 0 && *eptr == Int32 || typeid == JumpAddress && *eptr == ReferenceID)
			// {
			// 	// it's ok
			// 	continue;
			// }
			// else if (*eptr != typeid)
			// {
			// 	DOOM("call method-%d, but arg-%d is given typeid %d, required=%d\n", method_id, i, *eptr, typeid);
			// }
		}

		uchar* septr = eptr; // stack vals pointer.

		if (new_obj_id > 0)
		{
			uchar this_typeid = ReadByte;
			short aux = ReadShort;
			DIEIF(this_typeid != ReferenceID)
				DOOM("newobj call but this pointer is %d\n", this_typeid)
				* sptr = ReferenceID;
			As(sptr + 1, int) = new_obj_id;
			sptr += get_val_sz(ReferenceID);
		}

		for (int i = (new_obj_id > 0 ? 1 : 0); i < n_args; ++i)
		{
			uchar typeid = ReadByte;
			short aux = ReadShort;
			uchar sz = get_val_sz(typeid);
			if (typeid == JumpAddress) {
				if (aux == -1) DOOM("jump address but bad instantiate class");
				cls_id[aux_init] = aux;
				auxptr[aux_init] = sptr;
				if (*septr == ReferenceID)
				{
					cpy_obj_id[aux_init] = As(septr + 1, int);
				}
				else
				{
					DOOM("not supported arg push for jumpaddress from type_%d", *septr);
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

			// if (typeid == 0 && *septr == Int32)
			// {
			// 	//load int as boolean.
			// 	sptr[0] = 0; //boolean type.
			// 	sptr[1] = septr[1];
			// 	sptr += get_val_sz(Boolean);
			// 	septr += STACK_STRIDE;
			// }
			// else {
			// 	if (typeid != *septr) DOOM("WTF?");
			// 	memcpy(sptr, septr, sz);
			// 	sptr += sz;
			// 	septr += STACK_STRIDE;
			// }
		}
		*reptr = eptr; //pop arguments for previous stack.
	}
	// initialize vars:
	my_stack->vars = sptr;
	short n_vars = ReadShort;

	for (int i = (new_obj_id >= 0 ? 1 : 0); i < n_vars; ++i)
	{
		uchar typeid = ReadByte;
		short aux = ReadShort;
		if (typeid == JumpAddress) {
			if (aux == -1) DOOM("jump address but bad instantiate class");
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
			if (obj_ptr->clsid != clsid) DOOM("copy from bad class_%d, expected cls_%d", obj_ptr->clsid, clsid);
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
		uchar* eptr = my_stack->evaluation_pointer; // pointer to evaluation stack.

		uchar ic = ReadByte;
		il_cnt += 1;

		DBG("ic=%X (%d): ", ic, il_cnt);

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
				if (*eptr != Int32) DOOM("Stack value is not int32 for IL_Newarr...");
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
			DIEIF(!(*val1p <= 7 || *val1p == ReferenceID)) DOOM("not supported branch operand type");
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
			DIEIF(*val2p != *val1p)
				DOOM("comparison operands not same type?");

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
				DOOM("Unsupported type for comparison\n");
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
			DIEIF(*eptr != Address)
				DOOM("IL_Ldind as typeid: %d, but stack is %d not address.\n", typeid, *eptr);

			DIEIF(TypedAddrGetType(eptr) != typeid)
				DOOM("Ldind bad type, required %d, address refers to %d", typeid, TypedAddrGetType(eptr));

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

			DIEIF(*eptr != Address)
			{
				DOOM("IL_Stind as typeid: %d, but stack is %d not address.\n", typeid, *eptr);
			}

			// DIEIF(TypedAddrGetType(eptr) != typeid)
			// 	DOOM("Stind bad type, store as %d, target %d", typeid, TypedAddrGetType(eptr));

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
			default: DOOM("unrecognized.");
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
			default: DOOM("unrecognized.");
			}

			// if (typeid1<8 )
			DIEIF(ctype1 != ctype2)
			{
				DOOM("Type mismatch in arithmetic operation\n");
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
				default: DOOM("Unsupported operation for Int32\n");
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
				default: DOOM("Unsupported operation for Single\n");
					return;
				}
				DBG
				("IL_Arithmetic float operation: %02X, %f=>%f=>%f\n", op, a, b, result);

				PUSH_STACK_FLOAT_D(result);
				break;
			}
			default:
				DOOM("Unsupported type for arithmetic operation typeid=%d\n", typeid1);
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
				DOOM("Unsupported type for neg operation typeid=%d\n", typeid);
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
			DIEIF(typeid != Int32) { DOOM("Unsupported type for not operation typeid=%d\n", typeid); }

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
			default: DOOM("Unsupported conversion to SByte\n");
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
			default: DOOM("Unsupported conversion to Byte\n");
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
			default: DOOM("Unsupported conversion to Int16\n");
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
			default: DOOM("Unsupported conversion to UInt16\n");
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
			default: DOOM("Unsupported conversion to Int32\n");
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
			default: DOOM("Unsupported conversion to UInt32\n");
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
			default: DOOM("Unsupported conversion to Single\n");
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
			default: DOOM("Unsupported unsigned conversion to Single\n");
			}
			PUSH_STACK_FLOAT_D(value);
			DBG
			("ConvR_un: %02X\n", ic);
			break;
		}
		case 0x79: //initobj
		{
			POP;

			DBG
			("initobj format?");
			break;
			//on heap.
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
					DOOM("Error: Invalid builtin method ID: %d\n", method_id);
				}
				mtype = "builtin";
				builtin_arg0 = 0;
			}
			else
			{
				DOOM("Error: Unknown constructor type: %d\n", op_type);
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
					("stflds type_%d\n", *field_ptr);
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
						DBG
						("ldfld from address found obj_%d ", ref_id);
						if (ref_id == 0)
							DOOM("ldfld of nullpointer");
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
					if (ref_id == 0)
						DOOM("ldfld of nullpointer");
					obj = heap_obj[ref_id].pointer;
				}
				else
					DOOM("IL_Field requires Reference ID!\n");

				DIEIF(obj->clsid != aux)
					DOOM("Error: Object class ID mismatch\n");
				uchar* field_ptr = &obj->payload + offset;

				// Handle instance field operations
				if (ic == 0x7B || ic == 0x7C)
				{
					// Ldfld
					// POP object reference

					if (ic == 0x7B)
					{
						PUSH_STACK_INDIRECT(field_ptr);
						DBG
						("ldfld from %d(cls:%d, ofst:%d), type_%d\n", ref_id, aux, offset, *field_ptr);
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
					DBG
					("stfld to obj_%d(cls:%d, ofst:%d), type_%d\n", ref_id, aux, offset, *field_ptr);
				}
			}

			break;
		}

		case 0x8E: // Ldlen
		{
			POP;
			DIEIF(*eptr != ReferenceID)
			{
				DOOM("Ldlen: Expected array reference\n");
			}
			int arr_id = As(eptr + 1, int);
			if (arr_id == 0)
				DOOM("ldlen on nullpointer");
			struct array_val* arr = heap_obj[arr_id].pointer;
			DIEIF(arr->header != ArrayHeader) DOOM("obj_%d is not a array\n", arr_id);
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
			DIEIF(*eptr != ReferenceID)
			{
				DOOM("Ldelema: Expected array reference\n");
			}
			int arr_id = As(eptr + 1, int);
			if (arr_id == 0)
				DOOM("ldlen on nullpointer");
			struct array_val* arr = heap_obj[arr_id].pointer;
			DIEIF(arr->header != ArrayHeader) DOOM("obj_%d is not a array\n", arr_id);
			if (index < 0 || index >= arr->len)
			{
				DOOM("Ldelema: Index out of range\n");
			}
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
			DIEIF(*eptr != ReferenceID)
			{
				DOOM("Ldelem: Expected array reference\n");
			}
			int arr_id = As(eptr + 1, int);
			if (arr_id == 0)
				DOOM("ldelem on nullpointer");
			struct array_val* arr = heap_obj[arr_id].pointer;
			DIEIF(arr->header != ArrayHeader) DOOM("obj_%d is not a array\n", arr_id);
			if (index < 0 || index >= arr->len)
			{
				DOOM("Ldelem: Index out of range\n");
			}
			DIEIF(arr->typeid != typeid)
			{
				DOOM("Ldelem: Type mismatch\n");
			}
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
			DIEIF(*eptr != ReferenceID)
			{
				DOOM("Stelem: Expected array reference\n");
			}
			int arr_id = As(eptr + 1, int);
			if (arr_id == 0)
				DOOM("stelem on nullpointer");
			struct array_val* arr = heap_obj[arr_id].pointer;
			DIEIF(arr->header != ArrayHeader) DOOM("obj_%d is not a array\n", arr_id);
			// if (*value != typeid)
			//     DOOM("stelem: expected typeid_%d, get:{%d}", typeid, *value);
			if (index < 0 || index >= arr->len)
			{
				DOOM("Stelem: Index out of range\n");
			}

			int elem_size = get_type_sz(arr->typeid);
			uchar* elem_addr = &arr->payload + elem_size * index;

			if (arr->typeid == BoxedObject) {
				elem_addr[0] = value[0];
				copy_val(elem_addr, value);
			}
			else
			{
				DIEIF(arr->typeid != typeid) DOOM("array_%d is type %d but stelem as %d\n", arr_id, arr->typeid, typeid);
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
			uchar paramCnt = *ptr + 1;
			uchar* o_eptr = eptr;
			for (int i = 0; i < paramCnt; ++i)
				POP;

			POP;
			DIEIF(*eptr != ReferenceID) DOOM("this pointer should be reference id!");
			int instance_ref = As(eptr + 1, int);

			if (instance_ref == 0)
				DOOM("callvirt on nullpointer");
			// Get the object from the heap
			struct object_val* obj = (struct object_val*)heap_obj[instance_ref].pointer;
			DIEIF(obj->header != ObjectHeader)
				DOOM("this is not a object header?");
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

			DOOM("Cannot find vmethod %d for type %d\n", vmethod_id, obj->clsid);
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
					DOOM("Invalid built-in method ID: %d\n", method_id);
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
				DOOM("Invalid built-in method ID: %d\n", method_id);
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
					DOOM("bad comparison op_%d for single\n", ic);
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

			DIEIF(typeid != 6)
				DOOM("IL_Switch requires int, actual=%d\n", typeid);
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
			DOOM("Unknown instruction: 0x%02X\n", ic);
		}

		DIEIF(ptr >= virt_ptr) DOOM("bad program counter!");
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
	if (obj_id < 0 || obj_id >= heap_newobj_id)
		DOOM("invalid reference id?\n")
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
				if (typeid != *ptr) DOOM("bad builtin_cls %d on obj_%d", b_clsid, obj_id);
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

	// Reset all new_id to -1
	for (int i = 1; i < heap_newobj_id; i++)
		heap_obj[i].new_id = -1;


	// Start traversal from object 1 (root object)
	DBG("mark root: ");
	mark_object(1);

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
				if (*ref >= heap_newobj_id)
					DOOM("WTF?");
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
		if (*header != ArrayHeader && *header != ObjectHeader && *header != StringHeader)
			DOOM("bad heap header! header=%d?", *header);
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
	DBG("Heap cleanup complete. New heap size: %d\n", lastobj);


	for (int i = 1; i < heap_newobj_id; ++i)
	{
		uchar* header = heap_obj[i].pointer;
		if (*header != ArrayHeader && *header != ObjectHeader && *header != StringHeader)
			DOOM("bad heap header! header=%d?", *header);
	}
}


void vm_sort_slots();

void vm_run(int iteration)
{
	if (snapshot_state == 0)
		DOOM("Must update machine snapshot state before new iteration.");

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

// layout: iterations 4B|payload{1B cid, 1B typeid, NB val}|

void vm_put_upper_memory(uchar* buffer, int size)
{
	uchar* ptr = buffer;
	int iters = ReadInt;

	while (ptr < buffer + size)
	{
		int cid = ReadShort;
		uchar* field_ptr = statics_val_ptr + cartIO_layout_ptr[cid];
		uchar type_id = *field_ptr;
		uchar put_tid = ReadByte;
		if (type_id != put_tid)
			DOOM("put cart_io:%d expected type %d, recv:%d\n", cid, type_id, put_tid);
		memcpy(field_ptr + 1, ptr, get_type_sz(type_id));
		ptr += get_type_sz(type_id);
	}
}

int lowerUploadSz;
uchar* vm_get_lower_memory()
{
	if (new_stack_depth != 0)
		DOOM("Must perform get_lower_memory after VM execution!");
	uchar* lowerUpload = stack0;
	*((int*)lowerUpload) = iterations;
	uchar* lptr = lowerUpload + 4;
	// only cartIO vals are uploaded, and primitive only.
	for (int i = 0; i < cartIO_N; ++i)
	{
		if (cart_IO_stored[i / 32] & (1U << (i % 32)))
		{
			//value modified.
			As(lptr, short) = i; // which io is modified.
			lptr += 2;
			uchar* field_ptr = statics_val_ptr + cartIO_layout_ptr[i];
			uchar type_id = *field_ptr;
			int t_sz = get_val_sz(type_id);
			memcpy(lptr, field_ptr, t_sz); // modified to what?
			lptr += t_sz;
		}
	}
	lowerUploadSz = lptr - lowerUpload;
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
	if (myslot >= SLOT_NUMBER)
		DOOM("device IO buffer slots overflown!");
	writing_buf->N_slots += 1;
	int myoffset = writing_buf->offset;
	if (writing_buf->offset + size > BUF_SZ)
		DOOM("device IO buffer size overflown!");
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



///
///
///    ┳┓  •┓ •    ┏      •      •     ┓             •     
///    ┣┫┓┏┓┃╋┓┏┓  ╋┓┏┏┓┏╋┓┏┓┏┓  ┓┏┳┓┏┓┃┏┓┏┳┓┏┓┏┓╋┏┓╋┓┏┓┏┓┏
///    ┻┛┗┻┗┗┗┗┛┗  ┛┗┻┛┗┗┗┗┗┛┛┗  ┗┛┗┗┣┛┗┗ ┛┗┗┗ ┛┗┗┗┻┗┗┗┛┛┗┛
///                                  ┛                     
///    

// PUSH
#define PUSH_STACK_INT8(val) **reptr = SByte; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_UINT8(val) **reptr = Byte; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_INT16(val) **reptr = Int16; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_UINT16(val) **reptr = UInt16; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_INT(val) **reptr = Int32; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_UINT(val) **reptr = UInt32; As(*reptr + 1, int) = val; *reptr+=8;
#define PUSH_STACK_FLOAT_D(val) **reptr = Single; As(*reptr + 1, float) = val; *reptr+=8;

#define PUSH_STACK_FLOAT_M(val) { **reptr = Single; int iv = *(int*)(&(val)); As(*reptr + 1, int) = *(int*)(&(iv)); *reptr+=8; }

// not on reference id: it's heap object ID, not address!!!
#define PUSH_STACK_REFERENCEID(val) **reptr = ReferenceID; As(*reptr + 1, int) = val; *reptr+=8;

#undef PUSH_STACK_INDIRECT
#define POP {(*reptr)-=8;}

// Helper functions 
#define bool uchar
#define true 1
#define false 0

void push_int(uchar** reptr, int value) {
	PUSH_STACK_INT(value);
}

void push_float(uchar** reptr, float value) {
	PUSH_STACK_FLOAT_D(value);
}

void push_bool(uchar** reptr, bool value) {
	PUSH_STACK_INT8(value);
}

int pop_int(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != Int32) {
		DOOM("Type mismatch: expected Int32, got %d\n", typeid);
	}
	return *(int*)(*reptr + 1);
}

float pop_float(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != Single) {
		DOOM("Type mismatch: expected Single, got %d\n", typeid);
	}
	int itmp = *(int*)(*reptr + 1);
	float tmp = *(float*)(&itmp);
	return tmp;
}

bool pop_bool(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != 0) {  // Assuming 0 is the typeid for Boolean
		DOOM("Type mismatch: expected Boolean, got %d\n", typeid);
	}
	return *(bool*)(*reptr + 1);
}

// New helper functions for other types
short pop_short(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != Int16) {
		DOOM("Type mismatch: expected Int16, got %d\n", typeid);
	}
	return *(short*)(*reptr + 1);
}

char pop_sbyte(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != SByte) {
		DOOM("Type mismatch: expected SByte, got %d\n", typeid);
	}
	return *(char*)(*reptr + 1);
}

unsigned char pop_byte(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != Byte) {
		DOOM("Type mismatch: expected Byte, got %d\n", typeid);
	}
	return *(*reptr + 1);
}

long long pop_long(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != 9) {  // Assuming 9 is the typeid for Int64
		DOOM("Type mismatch: expected Int64, got %d\n", typeid);
	}
	return *(long long*)(*reptr + 1);
}

int pop_reference(uchar** reptr) {
	POP;
	uchar typeid = **reptr;
	if (typeid != ReferenceID) {
		DOOM("Type mismatch: expected ReferenceID, got %d\n", typeid);
	}
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
			if (end_brace && (end_brace - format_ptr) <= 3) {  // Max 2 digits for index
				char index_str[3] = { 0 };
				strncpy(index_str, format_ptr + 1, end_brace - format_ptr - 1);
				int index = atoi(index_str);

				if (index >= 0 && index < len_args) {
					uchar* heap_val_ptr = arg_ptr[index];
				retry:
					uchar type_id = *heap_val_ptr;
					uchar* payload = &heap_val_ptr[1];  // Skip the header byte

					switch (type_id) {
					case SByte:
						result_ptr += sprintf(result_ptr, "%d", *(char*)payload);
						break;
					case Byte:
						result_ptr += sprintf(result_ptr, "%u", *(unsigned char*)payload);
						break;
					case Int16:
						result_ptr += sprintf(result_ptr, "%d", *(short*)payload);
						break;
					case UInt16:
						result_ptr += sprintf(result_ptr, "%u", *(unsigned short*)payload);
						break;
					case Int32:
						result_ptr += sprintf(result_ptr, "%d", *(int*)payload);
						break;
					case UInt32:
						result_ptr += sprintf(result_ptr, "%u", *(unsigned int*)payload);
						break;
					case Single:
						result_ptr += sprintf(result_ptr, "%f", *(float*)payload);
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
						if (str_id == 0) break; //null.
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
		*result_ptr++ = *format_ptr++;
	}

	*result_ptr = '\0';  // Null-terminate the result string
}

void do_job(uchar** reptr, int len, uchar** arg_ptr)
{
	int format_str_id = pop_reference(reptr);
	if (format_str_id == 0)
		DOOM("format string is nullpointer?");
	uchar* header = heap_obj[format_str_id].pointer;
	if (*header != StringHeader)
		DOOM("format not a string!");

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
	if (args_array_id == 0)
		DOOM("format argument is nullpointer array?");
	uchar* args[16];
	uchar* header = heap_obj[args_array_id].pointer;
	if (*header != ArrayHeader)
		DOOM("args not a array!");
	struct array_val* arr = header;
	if (arr->typeid != BoxedObject)
		DOOM("args not an object array!");
	for (int i = 0; i < arr->len; ++i)
		args[i] = &arr->payload + i * get_type_sz(BoxedObject);
	do_job(reptr, arr->len, args);
}

void builtin_String_Concat_2(uchar** reptr) {
	int str2_id = pop_reference(reptr);
	int str1_id = pop_reference(reptr);

	if (str2_id == 0 || str1_id == 0) DOOM("concat of nullpointer");
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

	if (str3_id == 0 || str2_id == 0 || str1_id == 0) DOOM("concat of nullpointer");
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

	if (str4_id == 0 || str3_id == 0 || str2_id == 0 || str1_id == 0) DOOM("concat of nullpointer");
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

	if (str_id == 0) DOOM("substring of nullpointer");
	struct string_val* str = (struct string_val*)heap_obj[str_id].pointer;
	if (*(uchar*)str != StringHeader)
		DOOM("substring require string");

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
	if (*(uchar*)str != StringHeader)
		DOOM("substring require string");

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
	if (*header != ArrayHeader)
		DOOM("args not a array!");
	struct array_val* arr = header;
	if (arr->typeid != Byte)
		DOOM("array is not byte[]");

	enter_critical();
	int n_offset = writing_buf->offset;
	writing_buf->offset += arr->len;
	leave_critical();

	memcpy(&writing_buf->payload, &arr->payload, arr->len);

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
	if ((int)header == -1)
		DOOM("write event must not be null");

	if (*header != ArrayHeader)
		DOOM("args not a array!");
	struct array_val* arr = header;
	if (arr->typeid != Byte)
		DOOM("array is not byte[]");

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
	if (*header != ArrayHeader)
		DOOM("args not a array!");
	struct array_val* arr = header;
	if (arr->typeid != Byte)
		DOOM("array is not byte[]");

	// don't have to have same snapshot layout.
	enter_critical();
	int n_offset = writing_buf->offset;
	writing_buf->offset += arr->len;
	leave_critical();

	memcpy(&writing_buf->payload, &arr->payload, arr->len);

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
void builtin_ValueTuple2_ctor(uchar** reptr) {

	struct stack_frame_header* my_stack = stack_ptr[new_stack_depth - 1];
	if (my_stack->evaluation_st_ptr > *reptr)
		DOOM("WTF?")
		uchar* before = *reptr;
	POP;
	uchar* v2 = *reptr;
	POP;
	uchar* v1 = *reptr;

	struct object_val* tuple;
	if (!builtin_arg0) {
		POP;
		uchar* addr = *reptr;
		if (*addr != Address) DOOM("valuetuple need a address to init val's jmp address");
		uchar* jmp = TypedAddrAsValPtr(addr);
		if (*jmp != JumpAddress) DOOM("valuetuple need a jump_address (to the valuetuple reference)");
		// Set the fields of the tuple
		tuple = TypedAddrAsValPtr(jmp);
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
	DOOM("Not implemented");
}

void builtin_ValueTuple4_ctor(uchar** reptr) {
	DOOM("Not implemented");
}

void builtin_RuntimeHelpers_InitializeArray(uchar** reptr) {
	POP;
	// address
	if (**reptr != Address) DOOM("require address for arg2");
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
	DIEIF(typeid != 6 && typeid != Address) { DOOM("Bad input type, got %d", typeid) }

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
	DIEIF(typeid != 4 && typeid != Address) { DOOM("Bad input type, got %d", typeid) }

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
	DIEIF(typeid != 8 && typeid != Address) { DOOM("Bad input type, got %d", typeid) }

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

void delegate_ctor(uchar** reptr, short clsid)
{
	POP;
	if (**reptr != MethodPointer) DOOM("require a method pointer!");
	struct method_pointer* mp = *reptr + 1;
	if (mp->type == 1 && mp->id >= methods_N) {
		DOOM("invalid custom method id_%d\n", mp->id);
	}
	else if (mp->type == 0)
		DOOM("builtin method as action not supported");
	int obj_id = pop_reference(reptr);

	// Set the fields of the Action
	struct object_val* del = (struct object_val*)heap_obj[builtin_arg0].pointer;
	uchar* heap = (&del->payload);
	del->clsid = clsid; // identifier for Action/Func.
	HEAP_WRITE_REFERENCEID(obj_id);
	HEAP_WRITE_INT(mp->id);
}

void delegate_ivk(uchar** reptr, unsigned short clsid, int argN)
{
	// stack layout: action this pointer|arg0
	for (int i = 0; i < argN; ++i)
		POP;
	uchar* evptr = *reptr;
	int refid = pop_reference(reptr);
	struct object_val* action = (struct object_val*)heap_obj[refid].pointer;
	if (action->clsid != clsid)
		DOOM("not an required delegate type:%d\n", clsid);

	// Extract the object and method pointer
	int this_id = *(int*)(&action->payload + 1);
	int method_id = *(int*)(&action->payload + get_val_sz(Int32) + 1);

	// Push the object (this pointer) and argument
	PUSH_STACK_REFERENCEID(this_id); //we just replaced "this" pointer.
	*reptr = evptr;

	DBG("delegate obj_%d invoke method_%d\n", refid, method_id);
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
	unsigned short value = (unsigned short)pop_int(reptr);
	int array_id = newarr(2, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	*(short*)(&arr->payload) = *(short*)&value;
	PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_UInt32(uchar** reptr) {
	unsigned int value = (unsigned int)pop_int(reptr);
	int array_id = newarr(4, Byte);
	struct array_val* arr = heap_obj[array_id].pointer;
	*(int*)(&arr->payload) = value;
	PUSH_STACK_REFERENCEID(array_id);
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
	builtin_methods[bn++] = builtin_Int32_ToString; //79
	builtin_methods[bn++] = builtin_Int16_ToString; //80
	builtin_methods[bn++] = builtin_Single_ToString; //81

	builtin_methods[bn++] = builtin_Action_ctor; //82
	builtin_methods[bn++] = builtin_Action_Invoke; //83
	builtin_methods[bn++] = builtin_Action1_ctor; //84
	builtin_methods[bn++] = builtin_Action1_Invoke; //85
	builtin_methods[bn++] = builtin_Action2_ctor; //86
	builtin_methods[bn++] = builtin_Action2_Invoke; //87
	builtin_methods[bn++] = builtin_Action3_ctor; //88
	builtin_methods[bn++] = builtin_Action3_Invoke; //89
	builtin_methods[bn++] = builtin_Action4_ctor; //90
	builtin_methods[bn++] = builtin_Action4_Invoke; //91
	builtin_methods[bn++] = builtin_Action5_ctor; //92
	builtin_methods[bn++] = builtin_Action5_Invoke; //93
	builtin_methods[bn++] = builtin_Func1_ctor; //94
	builtin_methods[bn++] = builtin_Func1_Invoke; //95
	builtin_methods[bn++] = builtin_Func2_ctor; //96
	builtin_methods[bn++] = builtin_Func2_Invoke; //97
	builtin_methods[bn++] = builtin_Func3_ctor; //98
	builtin_methods[bn++] = builtin_Func3_Invoke; //99
	builtin_methods[bn++] = builtin_Func4_ctor; //100
	builtin_methods[bn++] = builtin_Func4_Invoke; //101
	builtin_methods[bn++] = builtin_Func5_ctor; //102
	builtin_methods[bn++] = builtin_Func5_Invoke; //103
	builtin_methods[bn++] = builtin_Func6_ctor; //104
	builtin_methods[bn++] = builtin_Func6_Invoke; //105
	builtin_methods[bn++] = builtin_Console_WriteLine; //106

	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Boolean; //107
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Char; //108
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Int16; //109
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Int32; //110
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_Single; //111
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_UInt16; //112
	builtin_methods[bn++] = builtin_BitConverter_GetBytes_UInt32; //113
	builtin_methods[bn++] = builtin_BitConverter_ToBoolean; //114
	builtin_methods[bn++] = builtin_BitConverter_ToChar; //115
	builtin_methods[bn++] = builtin_BitConverter_ToInt16; //116
	builtin_methods[bn++] = builtin_BitConverter_ToInt32; //117
	builtin_methods[bn++] = builtin_BitConverter_ToSingle; //118
	builtin_methods[bn++] = builtin_BitConverter_ToUInt16; //119
	builtin_methods[bn++] = builtin_BitConverter_ToUInt32; //120

	INFO("System builtin methods n=%d", bn);
	add_additional_builtins();

	if (bn >= NUM_BUILTIN_METHODS) {
		DOOM("Too many built-in methods! Increase NUM_BUILTIN_METHODS");
	}


}


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

void report_error(uchar* error_str) { DOOM("%s\n", error_str); }
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
	printf("====START TEST===\r\n");
	vm_set_program(bin, len);

	for (int i = 0; i < 100; ++i)
	{
		char buffer[38];
		vm_put_snapshot_buffer(buffer, 38);
		char bufferevent[8] = { i & 0xff,1,2,3,5,8,13,21 };
		vm_put_event_buffer(0, 0x80, bufferevent, 8);
		vm_run(i);

		uchar* mem = vm_get_lower_memory();
		int len = vm_get_lower_memory_size();
		if (stateCallback != 0)
			stateCallback(mem, len);
		// printf("====%d finished====\r\n", i);

		// system("pause");
		// print_hex(vm_get_lower_memory(), vm_get_lower_memory_size());
	}
}
#endif

#pragma pack(pop)
