using System;

// will generate a ExtraMethods.dll for reference.
namespace TEST;

public static class TESTCls
{
    public static int TestFunc(int input)
    { 
        // the function is actually implemented in cpp.
        throw new NotImplementedException();
    }
}