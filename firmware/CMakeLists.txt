# Fișierul CMake rădăcină al proiectului ESP-IDF — standard generat de framework, NU se editează
# logica de build aici; configurația specifică aplicației e în main/CMakeLists.txt și main/Kconfig.projbuild.
# Liniile de mai jos trebuie să rămână exact în această ordine pentru ca CMake/ESP-IDF să funcționeze corect.
cmake_minimum_required(VERSION 3.5)

include($ENV{IDF_PATH}/tools/cmake/project.cmake)
project(LifeAlertPlusESP32)