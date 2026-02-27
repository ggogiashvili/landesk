# NASM is required to build AOM
vcpkg_find_acquire_program(NASM)
get_filename_component(NASM_EXE_PATH ${NASM} DIRECTORY)
vcpkg_add_to_path(${NASM_EXE_PATH})

# Perl is required to build AOM
vcpkg_find_acquire_program(PERL)
get_filename_component(PERL_PATH ${PERL} DIRECTORY)
vcpkg_add_to_path(${PERL_PATH})

# Use AOM 3.9.1 on Windows: vcpkg's NASM lacks "multipass optimization" required by AOM 3.12.1
if(DEFINED ENV{USE_AOM_391} OR (VCPKG_TARGET_IS_WINDOWS AND VCPKG_TARGET_ARCHITECTURE STREQUAL "x64"))
    vcpkg_from_git(
        OUT_SOURCE_PATH SOURCE_PATH
        URL "https://aomedia.googlesource.com/aom"
        REF 8ad484f8a18ed1853c094e7d3a4e023b2a92df28 # 3.9.1
        PATCHES
            aom-uninitialized-pointer.diff
            aom-avx2.diff
            aom-install.diff
    )
else()
    vcpkg_from_git(
        OUT_SOURCE_PATH SOURCE_PATH
        URL "https://aomedia.googlesource.com/aom"
        REF 10aece4157eb79315da205f39e19bf6ab3ee30d0 # 3.12.1
        PATCHES
            aom-uninitialized-pointer.diff
            # aom-avx2.diff
            # Can be dropped when https://bugs.chromium.org/p/aomedia/issues/detail?id=3029 is merged into the upstream
            aom-install.diff
    )
endif()

set(aom_target_cpu "")
if(VCPKG_TARGET_IS_UWP OR (VCPKG_TARGET_IS_WINDOWS AND VCPKG_TARGET_ARCHITECTURE MATCHES "^arm"))
    # UWP + aom's assembler files result in weirdness and build failures
    # Also, disable assembly on ARM and ARM64 Windows to fix compilation issues.
    set(aom_target_cpu "-DAOM_TARGET_CPU=generic")
elseif(VCPKG_TARGET_IS_WINDOWS AND VCPKG_TARGET_ARCHITECTURE STREQUAL "x64")
    # Windows x64: vcpkg's NASM lacks "-Ox" (multipass) required by AOM's test_nasm().
    # Build without asm optimizations to avoid requiring a newer NASM.
    set(aom_target_cpu "-DAOM_TARGET_CPU=generic")
endif()

if(VCPKG_TARGET_ARCHITECTURE STREQUAL "arm" AND VCPKG_TARGET_IS_LINUX)
  set(aom_target_cpu "-DENABLE_NEON=OFF")
endif()

# On Windows x64: disable parallel configure to avoid the "ninja -v" step (which can exit 1 on some systems)
if(VCPKG_TARGET_IS_WINDOWS AND VCPKG_TARGET_ARCHITECTURE STREQUAL "x64")
  vcpkg_cmake_configure(
      SOURCE_PATH ${SOURCE_PATH}
      DISABLE_PARALLEL_CONFIGURE
      OPTIONS
          ${aom_target_cpu}
          -DENABLE_DOCS=OFF
          -DENABLE_EXAMPLES=OFF
          -DENABLE_TESTDATA=OFF
          -DENABLE_TESTS=OFF
          -DENABLE_TOOLS=OFF
  )
else()
  vcpkg_cmake_configure(
      SOURCE_PATH ${SOURCE_PATH}
      OPTIONS
          ${aom_target_cpu}
          -DENABLE_DOCS=OFF
          -DENABLE_EXAMPLES=OFF
          -DENABLE_TESTDATA=OFF
          -DENABLE_TESTS=OFF
          -DENABLE_TOOLS=OFF
  )
endif()

vcpkg_cmake_install()

vcpkg_copy_pdbs()

vcpkg_fixup_pkgconfig()

if(VCPKG_TARGET_IS_WINDOWS)
  vcpkg_replace_string("${CURRENT_PACKAGES_DIR}/lib/pkgconfig/aom.pc" " -lm" "")
  if(NOT VCPKG_BUILD_TYPE)
    vcpkg_replace_string("${CURRENT_PACKAGES_DIR}/debug/lib/pkgconfig/aom.pc" " -lm" "")
  endif()
endif()

# Move cmake configs
vcpkg_cmake_config_fixup(CONFIG_PATH lib/cmake/${PORT})

# Remove duplicate files
file(REMOVE_RECURSE ${CURRENT_PACKAGES_DIR}/debug/include
                    ${CURRENT_PACKAGES_DIR}/debug/share)

# Handle copyright
file(INSTALL ${SOURCE_PATH}/LICENSE DESTINATION ${CURRENT_PACKAGES_DIR}/share/${PORT} RENAME copyright)

vcpkg_fixup_pkgconfig()
