#pragma once
#include <string>

struct StatusModel
{
	bool nativeOnline = false;
	bool clientFound = false;
	unsigned int pid = 0;
	std::string processName;
	unsigned long long hwnd = 0;
};
