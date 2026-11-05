-- sorts clips by game, plays a sound, takes commands from the overlay
local obs = obslua
local ffi = require("ffi")

ffi.cdef [[
typedef struct { long left, top, right, bottom; } SHADOBS_RECT;
typedef struct { unsigned long size; SHADOBS_RECT monitor; SHADOBS_RECT work; unsigned long flags; } SHADOBS_MONITORINFO;
typedef int (*SHADOBS_ENUM)(void *, intptr_t);
int MultiByteToWideChar(unsigned int, unsigned long, const char *, int, wchar_t *, int);
int WideCharToMultiByte(unsigned int, unsigned long, const wchar_t *, int, char *, int, const char *, int *);
int PlaySoundW(const wchar_t *, void *, unsigned long);
void *GetForegroundWindow(void);
int InternalGetWindowText(void *, wchar_t *, int);
int GetWindowRect(void *, SHADOBS_RECT *);
void *MonitorFromWindow(void *, unsigned long);
int GetMonitorInfoW(void *, SHADOBS_MONITORINFO *);
int EnumWindows(SHADOBS_ENUM, intptr_t);
unsigned long GetWindowThreadProcessId(void *, unsigned long *);
unsigned long GetCurrentProcessId(void);
int PostMessageW(void *, unsigned int, uintptr_t, intptr_t);
int IsWindowVisible(void *);
]]

local winmm = ffi.load("winmm")

local CP_UTF8 = 65001
local SND_FILE_ASYNC = 0x20003
local MONITOR_NEAREST = 2
local WM_CLOSE = 0x0010
local SCENE = "Clips"
local GAME_SOURCE = "Game"
local DESKTOP_SOURCE = "Desktop"
local DESKTOP_FOLDER = "Desktop"
local COMMAND_MS = 200
local QUIT_RETRY_MS = 1000
local QUIT_POPUP_TRIES = 5

local capture_game = true
local sort_by_game = true
local save_sound = true

local game = nil
local connected = false
local desktop_visible = true
local applied_visible = nil
local status_dirty = true
local quitting = false
local quit_wait = 0
local quit_tries = 0
local sound_file = nil
local command_file = nil
local status_file = nil

local function wide(text)
    local size = ffi.C.MultiByteToWideChar(CP_UTF8, 0, text, -1, nil, 0)
    local buffer = ffi.new("wchar_t[?]", size)
    ffi.C.MultiByteToWideChar(CP_UTF8, 0, text, -1, buffer, size)
    return buffer
end

-- this one never messages the window, so it can't deadlock against a closing obs
local function window_title(window)
    local buffer = ffi.new("wchar_t[256]")
    local length = ffi.C.InternalGetWindowText(window, buffer, 256)
    if length <= 0 then
        return ""
    end
    local size = ffi.C.WideCharToMultiByte(CP_UTF8, 0, buffer, length, nil, 0, nil, nil)
    local text = ffi.new("char[?]", size)
    ffi.C.WideCharToMultiByte(CP_UTF8, 0, buffer, length, text, size, nil, nil)
    return ffi.string(text, size)
end

local function clean_name(name)
    name = name:gsub('[<>:"/\\|?*%c]', "")
    name = name:gsub("^[%s%.]+", "")
    name = name:gsub("[%s%.]+$", "")
    if #name > 80 then
        name = name:sub(1, 80):gsub("[\128-\255]+$", "")
    end
    if name == "" then
        return nil
    end
    return name
end

-- titles like "game v1.2.3 id=abc" change every launch so i use the exe for those
local function game_name(title, exe)
    local short = title:match("^(.-)%s+[-|:]%s") or title
    if short ~= "" and not short:find("%d+%.%d+") then
        local name = clean_name(short)
        if name then
            return name
        end
    end
    return clean_name((exe:gsub("%.[eE][xX][eE]$", "")))
end

-- only reads the window title so desktop capture never opens the game process
local function foreground_game()
    local window = ffi.C.GetForegroundWindow()
    if window == nil then
        return nil
    end
    local rect = ffi.new("SHADOBS_RECT")
    local info = ffi.new("SHADOBS_MONITORINFO")
    info.size = ffi.sizeof(info)
    local monitor = ffi.C.MonitorFromWindow(window, MONITOR_NEAREST)
    if ffi.C.GetWindowRect(window, rect) == 0 or ffi.C.GetMonitorInfoW(monitor, info) == 0 then
        return nil
    end
    local fills = rect.left <= info.monitor.left and rect.top <= info.monitor.top
        and rect.right >= info.monitor.right and rect.bottom >= info.monitor.bottom
    if not fills then
        return nil
    end
    local title = window_title(window)
    if title == "Program Manager" then
        return nil
    end
    return game_name(title, "")
end

local function write_status()
    local replay = obs.obs_frontend_replay_buffer_active() and "on" or "off"
    local text = "script=on\nreplay=" .. replay .. "\ngame=" .. (game or "") .. "\n"
    obs.os_quick_write_utf8_file(status_file, text, #text, false)
end

local function on_hooked(data)
    local title = obs.calldata_string(data, "title") or ""
    local exe = obs.calldata_string(data, "executable") or ""
    game = game_name(title, exe)
    desktop_visible = false
    status_dirty = true
end

local function on_unhooked(data)
    game = nil
    desktop_visible = true
    status_dirty = true
end

local function connect_game_source()
    local source = obs.obs_get_source_by_name(GAME_SOURCE)
    if source == nil then
        return false
    end
    local handler = obs.obs_source_get_signal_handler(source)
    obs.signal_handler_connect(handler, "hooked", on_hooked)
    obs.signal_handler_connect(handler, "unhooked", on_unhooked)
    obs.obs_source_release(source)
    return true
end

local function apply_desktop_visible()
    local source = obs.obs_get_source_by_name(SCENE)
    if source == nil then
        return false
    end
    local scene = obs.obs_scene_from_source(source)
    local item = obs.obs_scene_find_source(scene, DESKTOP_SOURCE)
    if item ~= nil then
        obs.obs_sceneitem_set_visible(item, desktop_visible)
    end
    obs.obs_source_release(source)
    return true
end

local function move_clip(path)
    local dir, name = path:match("^(.*)[/\\]([^/\\]+)$")
    if dir == nil then
        return
    end
    local folder = game or foreground_game() or DESKTOP_FOLDER
    local stamp = name:gsub("^Replay%s*", "")
    local target_dir = dir .. "/" .. folder
    obs.os_mkdirs(target_dir)
    local target = target_dir .. "/" .. folder .. " " .. stamp
    if obs.os_rename(path, target) ~= 0 then
        obs.script_log(obs.LOG_WARNING, "couldn't move clip to " .. target)
    end
end

local function on_saved()
    local path = obs.obs_frontend_get_last_replay()
    if sort_by_game and path ~= nil and path ~= "" then
        move_clip(path)
    end
    if save_sound then
        winmm.PlaySoundW(wide(sound_file), nil, SND_FILE_ASYNC)
    end
end

local function on_event(event)
    if event == obs.OBS_FRONTEND_EVENT_REPLAY_BUFFER_SAVED then
        on_saved()
    elseif event == obs.OBS_FRONTEND_EVENT_REPLAY_BUFFER_STARTED
        or event == obs.OBS_FRONTEND_EVENT_REPLAY_BUFFER_STOPPED then
        status_dirty = true
    end
end

-- a hidden tray window can't be closed from a non admin app, so obs closes itself
-- popups go first, closing the main window while one is up can hang obs on its way out
local function quit_obs()
    local me = ffi.C.GetCurrentProcessId()
    local main = {}
    local popups = {}
    local callback = ffi.cast("SHADOBS_ENUM", function(window, param)
        local id = ffi.new("unsigned long[1]")
        ffi.C.GetWindowThreadProcessId(window, id)
        if id[0] == me then
            local title = window_title(window)
            if title:match("^OBS %d") then
                main[#main + 1] = window
            elseif title ~= "" and ffi.C.IsWindowVisible(window) ~= 0 then
                popups[#popups + 1] = window
            end
        end
        return 1
    end)
    ffi.C.EnumWindows(callback, 0)
    callback:free()

    quit_tries = quit_tries + 1
    local targets = main
    if #popups > 0 and quit_tries <= QUIT_POPUP_TRIES then
        targets = popups
    end
    for _, window in ipairs(targets) do
        ffi.C.PostMessageW(window, WM_CLOSE, 0, 0)
    end
end
jit.off(quit_obs, true)

local function check_commands()
    if status_dirty then
        status_dirty = false
        write_status()
    end
    -- the first close can get eaten by a popup, so i keep asking until obs is gone
    if quitting then
        quit_wait = quit_wait + COMMAND_MS
        if quit_wait >= QUIT_RETRY_MS then
            quit_wait = 0
            quit_obs()
        end
    end
    if not obs.os_file_exists(command_file) then
        return
    end
    local text = obs.os_quick_read_utf8_file(command_file)
    obs.os_unlink(command_file)
    local command = text and text:match("(%a+)")
    if command == "start" then
        obs.obs_frontend_replay_buffer_start()
    elseif command == "stop" then
        obs.obs_frontend_replay_buffer_stop()
    elseif command == "save" then
        obs.obs_frontend_replay_buffer_save()
    elseif command == "quit" then
        quitting = true
        quit_obs()
    end
end

function script_description()
    return "Sorts clips by game, plays a sound when one saves, and takes commands from the ShadOBS overlay."
end

function script_defaults(settings)
    obs.obs_data_set_default_bool(settings, "capture_game", true)
    obs.obs_data_set_default_bool(settings, "sort_by_game", true)
    obs.obs_data_set_default_bool(settings, "save_sound", true)
end

function script_load(settings)
    capture_game = obs.obs_data_get_bool(settings, "capture_game")
    sort_by_game = obs.obs_data_get_bool(settings, "sort_by_game")
    save_sound = obs.obs_data_get_bool(settings, "save_sound")

    sound_file = script_path() .. "saved.wav"
    command_file = script_path() .. "command.txt"
    status_file = script_path() .. "status.txt"

    obs.obs_frontend_add_event_callback(on_event)
    obs.timer_add(check_commands, COMMAND_MS)
end

function script_unload()
    local text = "script=off\nreplay=off\ngame=\n"
    obs.os_quick_write_utf8_file(status_file, text, #text, false)
end

function script_tick(seconds)
    if not capture_game then
        return
    end
    if not connected then
        connected = connect_game_source()
    end
    if applied_visible ~= desktop_visible and apply_desktop_visible() then
        applied_visible = desktop_visible
    end
end
