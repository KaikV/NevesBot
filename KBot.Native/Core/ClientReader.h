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
#include <cstdio>

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

	// Runtime override for the position offset, discovered by the offset-hunter
	// (two-scan intersection). 0 = use the compiled-in DX/GL default. This is an
	// offset FROM THE MODULE BASE, so it stays valid across ASLR; it only breaks
	// when the client binary itself changes.
	ULONG_PTR GetActiveOffset() const noexcept
	{
		return m_customOffset != 0 ? m_customOffset : (IsDx() ? DxPositionOffset : GlPointerOffset);
	}
	bool HasCustomOffset() const noexcept { return m_customOffset != 0; }

	bool TrySetCustomPositionOffset(unsigned long long offsetFromBase, std::string& message) noexcept
	{
		if (offsetFromBase == 0)
		{
			m_customOffset = 0;
			m_valid = false;
			message = "Offset custom limpo; usando o padrao do build.";
			return true;
		}
		// Sanity: the triple must sit inside the main module to be a real position
		// field, not an out-of-range garbage hit. 128MB covers the DX build whose
		// confirmed offset is 0x37454E0 (~58MB) plus recalibrated offsets near it.
		if (offsetFromBase > 0x8000000ULL)
		{
			message = "Offset fora da faixa esperada (>128MB); provavel falso positivo.";
			return false;
		}
		m_customOffset = static_cast<ULONG_PTR>(offsetFromBase);
		m_valid = false; // force a fresh read on the next Poll so a bad offset errors out cleanly
		char buf[96];
		snprintf(buf, sizeof(buf), "Offset custom definido em 0x%llX (da base do modulo).", static_cast<unsigned long long>(m_customOffset));
		message = buf;
		return true;
	}

	void Initialize(unsigned int pid, HANDLE readHandle, const std::string& processName) noexcept
	{
		m_pid = pid;
		m_readHandle = readHandle;
		m_processName = processName;
		m_baseAddress = 0;
		m_valid = false;
		m_x = m_y = m_z = 0;
		m_customOffset = 0;
		ResetMemoryDump();
		ResetDeltaScan();
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
		ResetDeltaScan();
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
		if (m_customOffset != 0)
		{
			// Runtime override: the hunted address holds the X/Y/Z triple directly
			// (adjacent int32s), independent of the build's original layout.
			if (!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + m_customOffset), &x, sizeof(x)) ||
				!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + m_customOffset + YOffset), &y, sizeof(y)) ||
				!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + m_customOffset + ZOffset), &z, sizeof(z)))
			{
				SetMessage("Falha ao ler X/Y/Z no offset custom; o endereco pode ter mudado com uma atualizacao.");
				return;
			}
			m_x = x; m_y = y; m_z = z;
			m_valid = true;
			SetMessage("Posicao lida via offset custom (calibrado).");
			return;
		}
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

	// Delta-scan: automatic position-offset calibration. The bot does NOT need to
	// know its own coordinates - it only needs an address whose int32 triple moves
	// by exactly one tile when the character steps one tile. Three phases over the
	// same module memory:
	//   SNAP   - cache the whole module as int32 (baseline, character standing still)
	//   COMMIT - re-read after one step; any triple where EXACTLY one axis changed
	//            by +/-1..2 and the other two stayed put (|values| < 50000 on both
	//            sides) is a candidate position field
	//   STABLE - re-read while standing still; a real position field is frozen now,
	//            so candidates that kept moving are counters/tickers and get dropped
	bool TryDeltaSnap(std::string& message) noexcept
	{
		m_deltaS0.clear();
		m_deltaS1.clear();
		if (m_pid == 0 || m_readHandle == INVALID_HANDLE_VALUE)
		{
			message = "Sem handle de leitura.";
			return false;
		}
		ULONG_PTR base = 0;
		SIZE_T size = 0;
		if (!ResolveModule(base, size))
		{
			message = "Modulo principal nao encontrado.";
			return false;
		}
		if (size < 64 || size > 0x8000000) // under 128MB: match the custom-offset sanity ceiling
		{
			char buf[96];
			snprintf(buf, sizeof(buf), "Modulo com tamanho suspeito (0x%X bytes); delta-scan abortado.", size);
			message = buf;
			return false;
		}
		const SIZE_T chunk = SIZE_T(64) * 1024;
		std::vector<char> buffer(chunk + 8);
		for (SIZE_T start = 0; start < size; start += chunk)
		{
			const SIZE_T count = std::min(chunk + 8, size - start);
			if (!ReadMemory(reinterpret_cast<LPCVOID>(base + start), buffer.data(), count))
				continue; // partial/unreadable page: skip, keep the rest
			for (SIZE_T i = 0; i + 4 <= count; i += 4)
			{
				int value = 0;
				memcpy(&value, buffer.data() + i, sizeof(value));
				m_deltaS0.push_back(value);
			}
		}
		m_deltaS1 = m_deltaS0; // STABLE compares against the post-step read
		message = "Snapshot do modulo capturado para o delta-scan.";
		return !m_deltaS0.empty();
	}

	bool TryDeltaCommit(std::vector<unsigned long long>& offsetsFromBase, std::string& message) noexcept
	{
		offsetsFromBase.clear();
		m_deltaS1.clear();
		if (m_pid == 0 || m_readHandle == INVALID_HANDLE_VALUE)
		{
			message = "Sem handle de leitura.";
			return false;
		}
		if (m_deltaS0.empty())
		{
			message = "Sem snapshot anterior; rode SCAN_DELTA_SNAP primeiro.";
			return false;
		}
		ULONG_PTR base = 0;
		SIZE_T size = 0;
		if (!ResolveModule(base, size))
		{
			message = "Modulo principal nao encontrado.";
			return false;
		}
		const SIZE_T chunk = SIZE_T(64) * 1024;
		std::vector<char> buffer(chunk + 8);
		for (SIZE_T start = 0; start < size && offsetsFromBase.size() < 24; start += chunk)
		{
			const SIZE_T count = std::min(chunk + 8, size - start);
			if (!ReadMemory(reinterpret_cast<LPCVOID>(base + start), buffer.data(), count))
				continue;
			for (SIZE_T i = 0; i + 4 <= count; i += 4)
			{
				int value = 0;
				memcpy(&value, buffer.data() + i, sizeof(value));
				m_deltaS1.push_back(value);
				const SIZE_T triple = i / 4; // aligned int32 index of this word
				if (triple + 3 >= m_deltaS0.size() || triple + 3 >= m_deltaS1.size()) continue;
				const int x0 = m_deltaS0[triple],     y0 = m_deltaS0[triple + 1], z0 = m_deltaS0[triple + 2];
				const int x1 = m_deltaS1[triple],     y1 = m_deltaS1[triple + 1], z1 = m_deltaS1[triple + 2];
				const int dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
				const int changed = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
				if (changed != 1) continue; // a tile step moves exactly one axis
				const bool sane = (std::abs(x0) < 50000 && std::abs(y0) < 50000 && std::abs(z0) < 50000) &&
				                  (std::abs(x1) < 50000 && std::abs(y1) < 50000 && std::abs(z1) < 50000);
				if (!sane) continue;
				offsetsFromBase.push_back(triple * 4ULL);
			}
		}
		message = "Delta commit feito; candidatos onde um eixo mudou 1 tile.";
		return true;
	}

	bool TryVerifyDeltaStable(std::string& message) noexcept
	{
		std::vector<unsigned long long> stable;
		for (auto offset : m_deltaStableOffsets)
		{
			const SIZE_T triple = static_cast<SIZE_T>(offset / 4);
			if (triple + 3 >= m_deltaS1.size()) continue;
			int x = 0, y = 0, z = 0;
			if (!ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + offset), &x, sizeof(x)) ||
			    !ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + offset + 4), &y, sizeof(y)) ||
			    !ReadMemory(reinterpret_cast<LPCVOID>(m_baseAddress + offset + 8), &z, sizeof(z)))
				continue; // unreadable page now: drop, too risky
			if (x == m_deltaS1[triple] && y == m_deltaS1[triple + 1] && z == m_deltaS1[triple + 2])
				stable.push_back(offset); // frozen while standing still: real position field
		}
		m_deltaStableOffsets = std::move(stable);
		message = "Filtragem de estabilidade aplicada; contadores descartados.";
		return true;
	}

	void ResetDeltaScan() noexcept
	{
		m_deltaS0.clear();
		m_deltaS1.clear();
		m_deltaStableOffsets.clear();
	}

	const std::vector<unsigned long long>& DeltaStableOffsets() const noexcept { return m_deltaStableOffsets; }

	void RememberDeltaCandidates(const std::vector<unsigned long long>& candidates) noexcept { m_deltaStableOffsets = candidates; }

private:
	unsigned int m_pid = 0;
	HANDLE m_readHandle = INVALID_HANDLE_VALUE;
	std::string m_processName;
	ULONG_PTR m_baseAddress = 0;
	bool m_valid = false;
	int m_x = 0, m_y = 0, m_z = 0;
	ULONG_PTR m_customOffset = 0;
	std::string m_message;
	unsigned long long m_dumpAddress = 0;
	unsigned int m_dumpRemaining = 0;
	std::vector<int> m_deltaS0;
	std::vector<int> m_deltaS1;
	std::vector<unsigned long long> m_deltaStableOffsets;

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
