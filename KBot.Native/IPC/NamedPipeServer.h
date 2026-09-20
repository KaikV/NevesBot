#pragma once
#include <string>
#include <windows.h>
#include "../Core/ProcessManager.h"
#include "../Core/ClientReader.h"

class NamedPipeServer
{
public:
	NamedPipeServer(const std::string& pipeName, ProcessManager* pm) noexcept;
	~NamedPipeServer();
	void Run();

private:
	std::string m_pipeName;
	HANDLE m_pipe = INVALID_HANDLE_VALUE;
	ProcessManager* m_pm;
	ClientReader m_reader;
	void HandleClient();
};
