#pragma once

#include <string>
#include <iostream>

// Reader boundary for the selected client. Addresses are deliberately not
// embedded here: they must be confirmed for the exact PokeAlliance build
// before any memory read is enabled.
class ClientReader
{
public:
	void Initialize(unsigned int pid, bool processHandleOpened) noexcept
	{
		m_pid = pid;
		m_handleOpened = processHandleOpened;
		std::cout << "[ClientReader] Initializing pid=" << pid << '\n';
		std::cout << "[ClientReader] Process handle " << (processHandleOpened ? "opened" : "unavailable") << '\n';
		std::cout << "[ClientReader] Address provider not configured for this client build" << '\n';
		std::cout << "[ClientReader] Character state unavailable" << '\n';
	}
	void Reset() noexcept { m_pid = 0; m_handleOpened = false; }
	const char* GetStatus() const noexcept
	{
		if (m_pid == 0) return "NOT_ATTACHED";
		if (!m_handleOpened) return "PROCESS_HANDLE_UNAVAILABLE";
		return "NOT_CONFIGURED";
	}
	const char* GetMessage() const noexcept
	{
		if (m_pid == 0) return "Nenhum PID vinculado ao ClientReader.";
		if (!m_handleOpened) return "ProcessManager nao abriu um handle de consulta para o PID vinculado.";
		return "Nenhum AddressResolver ou perfil de enderecos confirmado existe para esta versao; estado do personagem indisponivel.";
	}
private:
	unsigned int m_pid = 0;
	bool m_handleOpened = false;
};
