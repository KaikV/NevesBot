#include "NamedPipeServer.h"
#include <iostream>
#include <thread>
#include <vector>
#include <sstream>
#include <iomanip>
#include <windows.h>
#include "../Models/StatusModel.h"
#include "../Core/ClientReader.h"

namespace
{
	std::string EscapeJson(const std::string& value)
	{
		std::ostringstream escaped;
		for (unsigned char ch : value)
		{
			switch (ch)
			{
			case '"': escaped << "\\\""; break;
			case '\\': escaped << "\\\\"; break;
			case '\n': escaped << "\\n"; break;
			case '\r': escaped << "\\r"; break;
			case '\t': escaped << "\\t"; break;
			default:
				if (ch < 0x20)
					escaped << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(ch);
				else
					escaped << ch;
			}
		}
		return escaped.str();
	}
}

NamedPipeServer::NamedPipeServer(const std::string& pipeName, ProcessManager* pm) noexcept
	: m_pipeName(pipeName), m_pm(pm)
{
}

NamedPipeServer::~NamedPipeServer()
{
	if (m_pipe != INVALID_HANDLE_VALUE)
	{
		CloseHandle(m_pipe);
		m_pipe = INVALID_HANDLE_VALUE;
	}
}

void NamedPipeServer::Run()
{
	std::string fullName = "\\\\.\\pipe\\" + m_pipeName;
	std::cout << "Named pipe server started: " << fullName << "\n";

	while (true)
	{
		m_pipe = CreateNamedPipeA(fullName.c_str(), PIPE_ACCESS_DUPLEX, PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
								  PIPE_UNLIMITED_INSTANCES, 4096, 4096, 0, nullptr);
		if (m_pipe == INVALID_HANDLE_VALUE)
		{
			std::cerr << "Failed to create named pipe instance, retrying...\n";
			std::this_thread::sleep_for(std::chrono::milliseconds(500));
			continue;
		}

		BOOL connected = ConnectNamedPipe(m_pipe, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
		if (connected)
		{
			HandleClient();
		}
		else
		{
			// client did not connect, close this instance and loop
			CloseHandle(m_pipe);
			m_pipe = INVALID_HANDLE_VALUE;
			std::this_thread::sleep_for(std::chrono::milliseconds(100));
		}
	}
}

void NamedPipeServer::HandleClient()
{
	char buffer[4096] = {0};
	DWORD read = 0;
	BOOL ok = ReadFile(m_pipe, buffer, sizeof(buffer) - 1, &read, nullptr);
	if (!ok || read == 0)
	{
		// client disconnected or error; ensure pipe instance closed and return to accept new clients
		DisconnectNamedPipe(m_pipe);
		CloseHandle(m_pipe);
		m_pipe = INVALID_HANDLE_VALUE;
		return;
	}

	std::string request(buffer, read);
	while (!request.empty() && (request.back() == '\r' || request.back() == '\n')) request.pop_back();

	std::string response;
	if (request == "PING")
	{
		response = "PONG\n";
	}
	else if (request == "GET_STATUS")
	{
		std::string proc = m_pm ? m_pm->GetProcessName() : std::string();

		StatusModel st;
		st.nativeOnline = true;
		if (m_pm)
		{
			st.clientFound = m_pm->IsRunning();
			st.pid = m_pm->GetPid();
			st.processName = proc;
		}
		else
		{
			st.clientFound = false;
			st.pid = 0;
			st.processName = proc;
		}

		m_reader.Poll();

		int posX = 0, posY = 0, posZ = 0;
		bool hasPos = m_reader.TryGetPosition(posX, posY, posZ);

		// simple JSON
		std::ostringstream ss;
		ss << "{\"nativeOnline\":" << (st.nativeOnline ? "true" : "false")
		   << ",\"clientFound\":" << (st.clientFound ? "true" : "false")
		   << ",\"pid\":" << st.pid
		   << ",\"processName\":\"" << EscapeJson(st.processName) << "\""
		   << ",\"hwnd\":" << (m_pm ? m_pm->GetWindowHandle() : 0)
		   << ",\"readerStatus\":\"" << m_reader.GetStatus() << "\""
		   << ",\"readerMessage\":\"" << EscapeJson(m_reader.GetMessage()) << "\""
		   << ",\"hasPosition\":" << (hasPos ? "true" : "false")
		   << ",\"posX\":" << posX
		   << ",\"posY\":" << posY
		   << ",\"posZ\":" << posZ << "}"
		;

		response = ss.str() + "\n";
	}
	else if (request == "GET_READER_STATUS")
	{
		m_reader.Poll();
		std::ostringstream ss;
		ss << "{\"status\":\"" << m_reader.GetStatus()
		   << "\",\"message\":\"" << EscapeJson(m_reader.GetMessage()) << "\"}\n";
		response = ss.str();
	}
	else if (request.rfind("ATTACH_PID ", 0) == 0)
	{
		std::istringstream input(request.substr(11));
		unsigned long pid = 0;
		std::string name;
		input >> pid >> name;
		const bool attached = m_pm && pid <= 0xFFFFFFFFUL && m_pm->AttachPid(static_cast<DWORD>(pid), name);
		if (attached) m_reader.Initialize(m_pm->GetPid(), m_pm->GetReadHandle(), m_pm->GetProcessName());
		else m_reader.Reset();
		response = attached ? "ATTACHED\n" : "ATTACH_FAILED\n";
	}
	else if (request == "DETACH_PID")
	{
		if (m_pm) m_pm->DetachPid();
		m_reader.Reset();
		response = "DETACHED\n";
	}
	else if (request.rfind("SEND_KEY ", 0) == 0)
	{
		bool sent = false;
		if (m_pm && m_pm->IsRunning())
		{
			HWND window = reinterpret_cast<HWND>(m_pm->GetWindowHandle());
			std::string key = request.substr(9);
			WORD virtualKey = 0;
			if (key == "UP") virtualKey = VK_UP;
			else if (key == "DOWN") virtualKey = VK_DOWN;
			else if (key == "LEFT") virtualKey = VK_LEFT;
			else if (key == "RIGHT") virtualKey = VK_RIGHT;
			else if (key == "W") virtualKey = 'W';
			else if (key == "A") virtualKey = 'A';
			else if (key == "S") virtualKey = 'S';
			else if (key == "D") virtualKey = 'D';

			if (window != nullptr && virtualKey != 0)
			{
				const LPARAM down = 1;
				const LPARAM up = 1 | (1 << 30) | (1u << 31);
				sent = PostMessageW(window, WM_KEYDOWN, virtualKey, down) != FALSE;
				PostMessageW(window, WM_KEYUP, virtualKey, up);
			}
		}
		response = sent ? "KEY_SENT\n" : "KEY_NOT_SENT\n";
	}
	else if (request.rfind("DUMP_START ", 0) == 0)
	{
		// DUMP_START <offsetFromAnchorDec> <byteCountDec>
		std::istringstream input(request.substr(11));
		long long offset = 0;
		unsigned int bytes = 0x1000;
		input >> offset >> bytes;

		m_reader.Poll();
		unsigned long long anchor = 0;
		std::string msg;
		const bool ok = m_reader.TryStartMemoryDump(offset, bytes, anchor, msg);
		std::ostringstream ss;
		ss << "{\"ok\":" << (ok ? "true" : "false")
		   << ",\"anchor\":\"0x" << std::hex << std::uppercase << anchor << std::dec << "\""
		   << ",\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request.rfind("DUMP_CHUNK ", 0) == 0)
	{
		std::istringstream input(request.substr(11));
		unsigned int chunk = 4096;
		input >> chunk;

		std::string hex;
		unsigned int remaining = 0;
		const bool got = m_reader.TryGetNextDumpChunk(hex, chunk, remaining);
		std::ostringstream ss;
		ss << "{\"got\":" << (got ? "true" : "false")
		   << ",\"remaining\":" << remaining
		   << ",\"hex\":\"" << EscapeJson(hex) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request == "DUMP_RESET")
	{
		m_reader.ResetMemoryDump();
		response = "RESET\n";
	}
	else if (request.rfind("SCAN_POSITION ", 0) == 0)
	{
		// SCAN_POSITION <x> <y> <z> -> JSON with candidate offsets from base.
		std::istringstream input(request.substr(14));
		int x = 0, y = 0, z = 0;
		input >> x >> y >> z;
		m_reader.Poll();

		std::vector<unsigned long long> candidates;
		unsigned long long scanned = 0;
		std::string msg;
		const bool ok = m_reader.TryScanForPosition(x, y, z, candidates, scanned, msg);

		std::ostringstream ss;
		ss << "{\"ok\":" << (ok ? "true" : "false")
		   << ",\"count\":" << candidates.size()
		   << ",\"scanned\":\"0x" << std::hex << std::uppercase << scanned << std::dec << "\"";
		if (candidates.size() > 24) candidates.resize(24); // keep within the 4KB pipe frame
		ss << ",\"candidates\":[";
		for (size_t i = 0; i < candidates.size(); ++i)
		{
			if (i) ss << ",";
			ss << "\"0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0')
			   << candidates[i] << std::dec << std::setfill(' ') << "\"";
		}
		ss << "],\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request.rfind("SET_POSITION_OFFSET ", 0) == 0)
	{
		// SET_POSITION_OFFSET <hexOffsetFromBase> -> apply a hunted offset at runtime.
		std::istringstream input(request.substr(20));
		input >> std::hex;
		unsigned long long offset = 0;
		input >> offset;

		std::string msg;
		const bool ok = m_reader.TrySetCustomPositionOffset(offset, msg);
		if (ok && offset != 0) m_reader.Poll(); // force a read so a bad offset fails fast

		std::ostringstream ss;
		ss << "{\"ok\":" << (ok ? "true" : "false")
		   << ",\"offset\":\"0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0')
		   << offset << std::dec << std::setfill(' ')
		   << "\",\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request == "SCAN_DELTA_SNAP")
	{
		m_reader.Poll();
		std::string msg;
		const bool ok = m_reader.TryDeltaSnap(msg);
		std::ostringstream ss;
		ss << "{\"ok\":" << (ok ? "true" : "false")
		   << ",\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request == "SCAN_DELTA_COMMIT")
	{
		m_reader.Poll();
		std::vector<unsigned long long> candidates;
		std::string msg;
		m_reader.TryDeltaCommit(candidates, msg);
		candidates.resize(24); // keep within the 4KB pipe frame
		m_reader.RememberDeltaCandidates(candidates); // verified by SCAN_DELTA_STABLE once the char stands still
		std::ostringstream ss;
		ss << "{\"count\":" << candidates.size()
		   << ",\"candidates\":[";
		for (size_t i = 0; i < candidates.size(); ++i)
		{
			if (i) ss << ",";
			ss << "\"0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0')
			   << candidates[i] << std::dec << std::setfill(' ') << "\"";
		}
		ss << "],\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request == "SCAN_DELTA_STABLE")
	{
		// PING: re-read the remembered candidates after the character stood still;
		// only frozen triples survive (real position, not counters).
		m_reader.Poll();
		std::string msg;
		m_reader.TryVerifyDeltaStable(msg);
		const auto& stable = m_reader.DeltaStableOffsets();
		std::ostringstream ss;
		ss << "{\"count\":" << stable.size()
		   << ",\"candidates\":[";
		for (size_t i = 0; i < stable.size(); ++i)
		{
			if (i) ss << ",";
			ss << "\"0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0')
			   << stable[i] << std::dec << std::setfill(' ') << "\"";
		}
		ss << "]}";
		response = ss.str() + "\n";
	}
	else if (request == "CLEAR_POSITION_OFFSET")
	{
		std::string msg;
		const bool ok = m_reader.TrySetCustomPositionOffset(0, msg);
		std::ostringstream ss;
		ss << "{\"ok\":" << (ok ? "true" : "false") << ",\"message\":\"" << EscapeJson(msg) << "\"}";
		response = ss.str() + "\n";
	}
	else if (request == "GET_OFFSET_STATUS")
	{
		std::ostringstream ss;
		ss << "{\"active\":" << (m_reader.HasCustomOffset() ? "true" : "false")
		   << ",\"offset\":\"0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0')
		   << m_reader.GetActiveOffset() << std::dec << std::setfill(' ') << "\"}";
		response = ss.str() + "\n";
	}
	else
	{
		response = "UNKNOWN\n";
	}

	DWORD written = 0;
	WriteFile(m_pipe, response.c_str(), static_cast<DWORD>(response.size()), &written, nullptr);
	FlushFileBuffers(m_pipe);
	DisconnectNamedPipe(m_pipe);
	CloseHandle(m_pipe);
	m_pipe = INVALID_HANDLE_VALUE;
}
