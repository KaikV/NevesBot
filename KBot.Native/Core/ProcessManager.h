#pragma once
#include <string>
#include <windows.h>

class ProcessManager
{
public:
	explicit ProcessManager(const std::string& targetName) noexcept;
	~ProcessManager();

	bool IsRunning();
	unsigned int GetPid() const noexcept;
	std::string GetProcessName() const noexcept;
	unsigned long long GetWindowHandle() const noexcept;
	bool HasProcessHandle() const noexcept;
	bool AttachPid(DWORD pid, const std::string& processName);
	void DetachPid();

private:
	std::string m_targetName;
	DWORD m_pid = 0;
	DWORD m_attachedPid = 0;
	HANDLE m_handle = INVALID_HANDLE_VALUE;
	HWND m_window = nullptr;

	// non-copyable
	ProcessManager(const ProcessManager&) = delete;
	ProcessManager& operator=(const ProcessManager&) = delete;

	void Refresh();
	void FindWindowByTitle();
};
