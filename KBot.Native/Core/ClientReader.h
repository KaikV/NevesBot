#pragma once

#include <string>
#include <string.h>
#include <iostream>
#include <vector>
#include <algorithm>
#include <sstream>
#include <iomanip>
#include <windows.h>
#include <TlHelp32.h>

// Reads the live character position out of the selected client.
// Only one confirmed address chain is used; everything else stays pending.
class ClientReader
{
public:
	// Confirmed reference offsets, per render build of PokeAlliance.
	//
	// GL build (_gl.exe): pointer chain.
	//   pointer = *(base + 0x0027D168)
	//   X/Y/Z   = *(int32*)(pointer + 0x0/0x4/0x8)
	static constexpr ULONG_PTR GlPointerOffset = 0x0027D168;
	//
	// DX build (_dx.exe): position lives directly in the main module.
	//   X = *(int32*)(base + 0x37454E0)
	//   Y = *(int32*)(base + 0x37454E4)
	//   Z = *(int32*)(base + 0x37454E8)
	static constexpr ULONG_PTR DxPositionOffset = 0x37454E0;
	static constexpr DWORD XOffset = 0x0;
	static constexpr DWORD YOffset = 0x4;
	static constexpr DWORD ZOffset = 0x8;

	void Initialize(unsigned int pid, HANDLE readHandle, const std::string& processName) noexcept
	{
		m_pid = pid;
		m_readHandle = readHandle;
		m_processName = processName;
		m_baseAddress = 0;
		m_valid = false;
		m_x = m_y = m_z = 0;
		ResetMemoryDump();
		SetMessage("Leitura aguardando o primeiro ciclo.");
		std::cout << "[ClientReader] Initializing pid=" << pid << '\n';
		std::cout << "[ClientReader] Read handle " << (readHandle != INVALID_HANDLE_VALUE ? "opened" : "unavailable") << '\n';
	}
	void Reset() noexcept
	{
		m_pid = 0;
		m_readHandle = INVALID_HANDLE_VALUE;
		m_processName.clear();
		m_baseAddress = 0;
		m_valid = false;
		m_x = m_y = m_z = 0;
		SetMessage("Nenhum PID vinculado ao ClientReader.");
	}

	// Refresh the cached position. Called before every GET_STATUS.
	void Poll() noexcept
	{
		m_valid = false;
		if (m_pid == 0 || m_readHandle == INVALID_HANDLE_VALUE)
		{
			SetMessage(m_pid == 0 ? "Nenhum PID vinculado ao ClientReader."
			                     : "ProcessManager nao abriu um handle de leitura (PROCESS_VM_READ) para o PID.");
			return;
		}
		if (!ResolveBase())
		{
			SetMessage("Base do modulo principal nao encontrada nesta versao do cliente.");
			return;
		}
		int x = 0, y = 0, z = 0;
		if (IsDx())
		{
			// DX: position is stored directly in the main module at base + DxPositionOffset.
			if (!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + DxPositionOffset), &x, sizeof(x)) ||
				!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + DxPositionOffset + YOffset), &y, sizeof(y)) ||
				!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + DxPositionOffset + ZOffset), &z, sizeof(z)))
			{
				SetMessage("Falha ao ler X/Y/Z direto em base + 0x37454E0; offset DX possivelmente mudou.");
				return;
			}
			m_x = x; m_y = y; m_z = z;
			m_valid = true;
			SetMessage("Posicao lida com sucesso (build DX).");
			return;
		}
		// GL: pointer chain.
		SIZE_T pointer = 0;
		if (!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + GlPointerOffset), &pointer, sizeof(pointer)))
		{
			SetMessage("Falha ao ler o ponteiro em base + 0x0027D168; offset possivelmente mudou.");
			return;
		}
		if (pointer == 0)
		{
			SetMessage("Ponteiro nulo lido; personagem ainda nao carregado ou offset deslocado.");
			return;
		}
		if (!ReadMemory(reinterpret_cast<LPCVOID>(pointer + XOffset), &x, sizeof(x)) ||
			!ReadMemory(reinterpret_cast<LPCVOID>(pointer + YOffset), &y, sizeof(y)) ||
			!ReadMemory(reinterpret_cast<LPCVOID>(pointer + ZOffset), &z, sizeof(z)))
		{
			SetMessage("Ponteiro valido, mas falha ao ler X/Y/Z; offset possivelmente mudou.");
			return;
		}
		m_x = x; m_y = y; m_z = z;
		m_valid = true;
		SetMessage("Posicao lida com sucesso.");
	}

	const char* GetStatus() const noexcept
	{
		if (m_pid == 0) return "NOT_ATTACHED";
		if (m_readHandle == INVALID_HANDLE_VALUE) return "PROCESS_HANDLE_UNAVAILABLE";
		return m_valid ? "READY" : "NOT_CONFIGURED";
	}
	const char* GetMessage() const noexcept { return m_message.c_str(); }

	bool TryGetPosition(int& x, int& y, int& z) const noexcept
	{
		if (!m_valid) return false;
		x = m_x; y = m_y; z = m_z;
		return true;
	}
	unsigned long long GetBaseAddress() const noexcept { return m_baseAddress; }

	// Offset-hunting support: dumps raw bytes around the same anchor the
	// confirmed position read uses. DX: base+0x37454E0 directly. GL: the
	// pointed-to character struct (where X/Y/Z live). Struct members of this
	// engine typically sit next to each other, so HP/battle state often shows
	// up within +/- a few hundred bytes of the position fields.
	bool TryStartMemoryDump(long long offsetFromBase, unsigned int byteCount,
	                        unsigned long long& anchorAddress, std::string& message) noexcept
	{
		m_dumpAddress = 0;
		m_dumpRemaining = 0;
		if (m_pid == 0 || m_readHandle == INVALID_HANDLE_VALUE)
		{
			message = "Sem handle de leitura.";
			return false;
		}
		if (!ResolveBase())
		{
			message = "Base do modulo nao encontrada.";
			return false;
		}
		unsigned long long anchor;
		if (IsDx())
		{
			anchor = m_baseAddress + static_cast<unsigned long long>(DxPositionOffset);
		}
		else
		{
			SIZE_T pointer = 0;
			if (!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + GlPointerOffset), &pointer, sizeof(pointer)))
			{
				message = "Falha ao ler ponteiro do personagem.";
				return false;
			}
			if (pointer == 0)
			{
				message = "Ponteiro do personagem nulo (nao carregado ou offset mudou).";
				return false;
			}
			anchor = reinterpret_cast<unsigned long long>(pointer);
		}
		const auto low = static_cast<long long>(0x2000);
		long long start = anchor + offsetFromBase;
		if (start < low) start = low;
		m_dumpAddress = static_cast<unsigned long long>(start);
		m_dumpRemaining = (byteCount > 0x10000) ? 0x10000 : byteCount;
		anchorAddress = anchor;
		message = "OK";
		return true;
	}

	bool TryGetNextDumpChunk(std::string& hexOut, unsigned int chunkBytes, unsigned int& remaining) noexcept
	{
		if (m_dumpRemaining == 0)
		{
			remaining = 0;
			return false;
		}
		auto count = static_cast<SIZE_T>(m_dumpRemaining < chunkBytes ? m_dumpRemaining : chunkBytes);
		std::vector<char> buffer(count, 0);
		size_t got = 0;
		if (ReadMemory(reinterpret_cast<LPCVOID>(m_dumpAddress), buffer.data(), count))
		{
			got = static_cast<size_t>(count);
		}
		std::ostringstream ss;
		for (size_t i = 0; i < got; ++i)
		{
			ss << std::hex << std::setw(2) << std::setfill('0')
			   << static_cast<int>(static_cast<unsigned char>(buffer[i]));
			if ((i + 1) % 16 == 0 && (i + 1) < got) ss << "\n";
			else if ((i + 1) < got) ss << " ";
		}
		hexOut = ss.str();
		m_dumpAddress += got;
		m_dumpRemaining -= static_cast<unsigned int>(got);
		remaining = m_dumpRemaining;
		return true;
	}

	void ResetMemoryDump() noexcept
	{
		m_dumpAddress = 0;
		m_dumpRemaining = 0;
	}

	// Offset-hunting: scans the whole main module for an exact, contiguous
	// int32 triple (x, y, z) - the minimap position read as three adjacent
	// fields. Returns every match as an offset from the module base, so when
	// the client updates and the confirmed offset breaks, the user types two
	// different positions (before/after one step) and the surviving candidate
	// is the real one.
	bool TryScanForPosition(int x, int y, int z,
	                        std::vector<unsigned long long>& offsetsFromBase,
	                        unsigned long long& scannedBytes,
	                        std::string& message) noexcept
	{
		offsetsFromBase.clear();
		scannedBytes = 0;
		if (m_pid == 0 || m_readHandle == INVALID_HANDLE_VALUE)
		{
			message = "Sem handle de leitura.";
			return false;
		}
		ULONG_PTR base = 0;
		SIZE_T size = 0;
		if (!ResolveModule(base, size))
		{
			message = "Base ou tamanho do modulo principal nao encontrados.";
			return false;
		}
		const unsigned char xb[4] = { unsigned char(x), unsigned char(x >> 8), unsigned char(x >> 16), unsigned char(x >> 24) };
		const unsigned char yb[4] = { unsigned char(y), unsigned char(y >> 8), unsigned char(y >> 16), unsigned char(y >> 24) };
		const unsigned char zb[4] = { unsigned char(z), unsigned char(z >> 8), unsigned char(z >> 16), unsigned char(z >> 24) };
		const SIZE_T chunk = SIZE_T(64) * 1024;
		std::vector<unsigned char> buffer(chunk + 8);
		for (SIZE_T start = 0; start < size && offsetsFromBase.size() < 24; start += chunk)
		{
			const SIZE_T count = std::min(chunk + 8, size - start);
			if (count < 12) break;
			if (!ReadMemory(reinterpret_cast<LPCVOID>(base + start), buffer.data(), count))
				continue;
			scannedBytes += 64 * 1024;
			for (SIZE_T i = 0; i + 12 <= count; i += 4)
			{
				if (memcmp(buffer.data() + i, xb, 4) == 0 &&
					memcmp(buffer.data() + i + 4, yb, 4) == 0 &&
					memcmp(buffer.data() + i + 8, zb, 4) == 0)
				{
					unsigned long long address = reinterpret_cast<unsigned long long>(base + start) + i;
					if (std::find(offsetsFromBase.begin(), offsetsFromBase.end(), address) == offsetsFromBase.end())
						offsetsFromBase.push_back(address - base);
				}
			}
		}
		message = "Escaneado modulo inteiro.";
		return true;
	}

private:
	unsigned int m_pid = 0;
	HANDLE m_readHandle = INVALID_HANDLE_VALUE;
	std::string m_processName;
	ULONG_PTR m_baseAddress = 0;
	bool m_valid = false;
	int m_x = 0, m_y = 0, m_z = 0;
	std::string m_message;
	unsigned long long m_dumpAddress = 0;
	unsigned int m_dumpRemaining = 0;

	void SetMessage(std::string value) noexcept { m_message = std::move(value); }

	bool IsDx() const noexcept
	{
		return m_processName.find("dx") != std::string::npos;
	}

	bool ReadMemory(LPCVOID address, void* destination, SIZE_T size) const noexcept
	{
		SIZE_T bytes = 0;
		return ReadProcessMemory(m_readHandle, address, destination, size, &bytes) == TRUE && bytes == size;
	}

	static std::wstring Stem(const std::wstring& path)
	{
		const auto slash = path.find_last_of(L"\\/");
		return slash == std::wstring::npos ? path : path.substr(slash + 1);
	}

	bool ResolveBase()
	{
		if (m_baseAddress != 0) return true;
		ULONG_PTR base = 0;
		SIZE_T size = 0;
		if (!ResolveModule(base, size)) return false;
		m_baseAddress = base;
		return m_baseAddress != 0;
	}

	// Resolves the main module's base address and declared size. Prefers the
	// module whose image name matches the attached process; falls back to the
	// first non-Unknown module with a known base.
	bool ResolveModule(ULONG_PTR& baseOut, SIZE_T& sizeOut) const
	{
		HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, m_pid);
		if (snap == INVALID_HANDLE_VALUE) return false;
		bool found = false;
		ULONG_PTR fallback = 0;
		SIZE_T fallbackSize = 0;
		bool haveFallback = false;
		MODULEENTRY32W entry;
		entry.dwSize = sizeof(entry);
		if (Module32FirstW(snap, &entry))
		{
			do
			{
				const std::wstring exe = Stem(entry.szExePath);
				if (!m_processName.empty() &&
					_wcsicmp(exe.c_str(), std::wstring(m_processName.begin(), m_processName.end()).c_str()) == 0)
				{
					baseOut = reinterpret_cast<ULONG_PTR>(entry.modBaseAddr);
					sizeOut = entry.modBaseSize;
					found = true;
					break;
				}
				if (!haveFallback && exe != L"Unknown" && entry.modBaseAddr != nullptr)
				{
					fallback = reinterpret_cast<ULONG_PTR>(entry.modBaseAddr);
					fallbackSize = entry.modBaseSize;
					haveFallback = true;
				}
			} while (Module32NextW(snap, &entry));
		}
		CloseHandle(snap);
		if (!found && haveFallback) { baseOut = fallback; sizeOut = fallbackSize; }
		return found || haveFallback;
	}
};
