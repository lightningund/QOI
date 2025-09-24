#include <iostream>
#include <fstream>
#include <array>

template <typename T, typename U, size_t len>
std::basic_ifstream<T>& operator>>(std::basic_ifstream<T>& stream, std::array<U, len>& arr) {
	static_assert(sizeof(U) % sizeof(T) == 0);
	for (int i = 0; i < len; ++i) {
		stream.get(reinterpret_cast<T*>(arr[i]), sizeof(U) / sizeof(T));
	}

	return stream;
}

int main() {
	std::ifstream input{"D:/CS_ALIAS/C++/QOI/testcard.qoi", std::ios_base::binary};
	std::array<char, 5> magic{};
	input >> magic;
	std::cout << std::boolalpha << input.fail() << "\n";
	for (int i = 0; i < 5; ++i) {
		std::cout << magic[i];
	}
	std::cout << "\n";
}