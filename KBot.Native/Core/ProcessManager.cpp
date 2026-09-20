#include "ProcessManager.h"
#include <TlHelp32.h>
#include <stdexcept>

ProcessManager::ProcessManager(const std::string& targetName) noexcept
	: m_targetName(targetName)
{
	Refresh();
}

ProcessManager::~ProcessManager()
{
	if (m_handle != INVALID_HANDLE_VALUE)
	{
		CloseHandle(m_handle);
		m_handle = INVALID_HANDLE_VALUE;
	}
	if (m_readHandle != INVALID_HANDLE_VALUE)
	{
		CloseHandle(m_readHandle);
		m_readHandle = INVALID_HANDLE_VALUE;
	}
}

void ProcessManager::Refresh()
{
	m_pid = 0;
	m_window = nullptr;
	if (m_handle != INVALID_HANDLE_VALUE)
	{
		CloseHandle(m_handle);
		m_handle = INVALID_HANDLE_VALUE;
	}
	if (m_readHandle != INVALID_HANDLE_VALUE)
	{
		CloseHandle(m_readHandle);
		m_readHandle = INVALID_HANDLE_VALUE;
	}

	if (m_attachedPid != 0)
	{
		HANDLE attached = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, m_attachedPid);
		DWORD exitCode = 0;
		if (attached != nullptr && GetExitCodeProcess(attached, &exitCode) && exitCode == STILL_ACTIVE)
		{
			m_pid = m_attachedPid;
			m_handle = attached;
			m_readHandle = OpenProcess(PROCESS_VM_READ, FALSE, m_attachedPid);
			FindWindowByTitle();
			return;
		}
		if (attached != nullptr) CloseHandle(attached);
		return;
	}
	if (m_targetName.empty()) return;

	HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
	if (snap == INVALID_HANDLE_VALUE) return;

	PROCESSENTRY32 pe;
	pe.dwSize = sizeof(pe);
	if (Process32First(snap, &pe))
	{
		do
		{
			// PROCESSENTRY32::szExeFile is a WCHAR[] when Unicode is defined
			std::wstring exeW(pe.szExeFile);
			std::wstring targetW(m_targetName.begin(), m_targetName.end());
			if (_wcsicmp(exeW.c_str(), targetW.c_str()) == 0)
			{
				m_pid = pe.th32ProcessID;
				break;
			}
		} while (Process32Next(snap, &pe));
	}

	CloseHandle(snap);
	// Resolve the top level client window for both process-name and title detection.
	// The HWND is required by the input and capture commands.
	FindWindowByTitle();

	if (m_pid != 0)
	{
		// Keep the status handle minimal; memory reads use a separate PROCESS_VM_READ handle.
		m_handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, m_pid);
		if (m_handle == nullptr)
		{
			m_handle = INVALID_HANDLE_VALUE;
		}
		m_readHandle = OpenProcess(PROCESS_VM_READ, FALSE, m_pid);
		if (m_readHandle == nullptr)
		{
			m_readHandle = INVALID_HANDLE_VALUE;
		}
	}
}

bool ProcessManager::IsRunning()
{
	Refresh();
	return m_pid != 0;
}

unsigned int ProcessManager::GetPid() const noexcept
{
	return static_cast<unsigned int>(m_pid);
}

std::string ProcessManager::GetProcessName() const noexcept
{
	return m_targetName;
}

bool ProcessManager::AttachPid(DWORD pid, const std::string& processName)
{
	if (pid == 0 || processName.empty()) return false;
	HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
	DWORD exitCode = 0;
	if (process == nullptr || !GetExitCodeProcess(process, &exitCode) || exitCode != STILL_ACTIVE)
	{
		if (process != nullptr) CloseHandle(process);
		return false;
	}
	CloseHandle(process);
	m_attachedPid = pid;
	m_targetName = processName;
	Refresh();
	return m_pid == pid && m_window != nullptr;
}

void ProcessManager::DetachPid()
{
	m_attachedPid = 0;
	m_targetName.clear();
	Refresh();
}

unsigned long long ProcessManager::GetWindowHandle() const noexcept
{
	return reinterpret_cast<unsigned long long>(m_window);
}

bool ProcessManager::HasProcessHandle() const noexcept
{
	return m_handle != nullptr && m_handle != INVALID_HANDLE_VALUE;
}

HANDLE ProcessManager::GetReadHandle() const noexcept
{
	return m_readHandle;
}

void ProcessManager::FindWindowByTitle()
{
	if (m_pid == 0) return;
	EnumWindows([](HWND window, LPARAM parameter) -> BOOL
	{
		auto* manager = reinterpret_cast<ProcessManager*>(parameter);
		if (!IsWindowVisible(window)) return TRUE;
		wchar_t title[512] = {};
		GetWindowTextW(window, title, 512);
		std::wstring text(title);
		if (text.empty()) return TRUE;
		DWORD pid = 0;
		GetWindowThreadProcessId(window, &pid);
		if (pid == 0) return TRUE;
		if (pid != manager->m_pid) return TRUE;
		manager->m_pid = pid;
		manager->m_window = window;
		return FALSE;
	}, reinterpret_cast<LPARAM>(this));
}
