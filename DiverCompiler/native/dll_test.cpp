#include <iostream>
#include <windows.h>

typedef int (*FunctionType)(int);  // Define a function prototype

int main(int argc, char* argv[])
{
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <DLL Name>" << std::endl;
        return 1;
    }

    const char* dllName =
            argv[1];  // The DLL name is expected as the first argument

    HINSTANCE hInstLibrary = LoadLibrary(dllName);

    if (hInstLibrary) {
        // Assuming the DLL has a function named "YourFunction"
        FunctionType fn = (FunctionType)GetProcAddress(hInstLibrary, "func_b");
        if (fn) {
            int result = fn(4);
            std::cout << "Result: " << result << std::endl;
        } else {
            std::cerr << "Function not found in the DLL!" << std::endl;
        }
        FreeLibrary(hInstLibrary);
    } else {
        std::cerr << "DLL not found: " << dllName << ", since error "
                  << GetLastError() << std::endl;
    }

    return 0;
}