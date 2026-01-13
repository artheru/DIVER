#include "appl/version.h"

#include "version.h"

VersionInfoC g_version_info;

void init_version_info()
{
    /* 清零，保证未填满部分为 0 */
    memset(&g_version_info, 0, sizeof(g_version_info));

#define COPY_FIXED(dst, src)          \
    do {                              \
        if (src) {                    \
            size_t _l = strlen(src);  \
            if (_l > sizeof(dst))     \
                _l = sizeof(dst);     \
            memcpy((dst), (src), _l); \
        }                             \
    } while (0)

#define STRINGIFY_HELPER(x) #x
#define STRINGIFY(x) STRINGIFY_HELPER(x)

    const char* PDN = STRINGIFY(PRODUCTION_NAME);

    COPY_FIXED(g_version_info.Tag, GIT_TAG);
    COPY_FIXED(g_version_info.Commit, GIT_COMMIT);
    COPY_FIXED(g_version_info.BuildTime, BUILD_TIME);
    COPY_FIXED(g_version_info.PDN, PDN);
}