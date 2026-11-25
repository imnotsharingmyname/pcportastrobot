// Instrumented kyty_gpu.so only: the game is killed, so write the profile every N s (KYTY_PGO_FILE).
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <thread>
extern "C" int  __llvm_profile_write_file(void);
extern "C" void __llvm_profile_set_filename(const char*);
__attribute__((constructor)) static void StartPgoDumps() {
	const char* file = std::getenv("KYTY_PGO_FILE");
	if (file == nullptr || *file == '\0') return;
	static const std::string target = file, temp = target + ".tmp";
	std::thread([] {
		for (;;) {
			std::this_thread::sleep_for(std::chrono::seconds(10));
			__llvm_profile_set_filename(temp.c_str());
			if (__llvm_profile_write_file() == 0) {
				std::error_code e;
				std::filesystem::rename(temp, target, e);
			}
		}
	}).detach();
}
