#!/usr/bin/env python3
"""Inspect/interact only with windows of the recorded, creation-time-verified test JVM."""
import argparse
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
from build import ROOT
from sandbox import BOX, windows_process

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--click', nargs=3, type=int, metavar=('HWND', 'RELATIVE_X', 'RELATIVE_Y'))
parser.add_argument('--close', type=int, metavar='HWND', help='Graceful WM_CLOSE of one owned test window only')
args = parser.parse_args()
record = json.loads((BOX / 'launcher.json').read_text(encoding='utf-8'))
pid = record['windowsPid']
if windows_process(pid) != record.get('creationFileTime'):
    raise SystemExit('Recorded sandbox JVM is no longer the same running process')
u = ctypes.windll.user32
u.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
u.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.IsWindowVisible.argtypes = [wintypes.HWND]
u.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
u.SetForegroundWindow.argtypes = [wintypes.HWND]
u.GetForegroundWindow.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.WindowFromPoint.restype = wintypes.HWND
windows = {}


def visit(handle, _):
    owner = wintypes.DWORD()
    u.GetWindowThreadProcessId(handle, ctypes.byref(owner))
    if owner.value == pid and u.IsWindowVisible(handle):
        title = ctypes.create_unicode_buffer(2048)
        u.GetWindowTextW(handle, title, len(title))
        rectangle = wintypes.RECT()
        u.GetWindowRect(handle, ctypes.byref(rectangle))
        box = (rectangle.left, rectangle.top, rectangle.right, rectangle.bottom)
        if box[2] - box[0] > 100 and box[3] - box[1] > 70:
            windows[int(handle)] = (title.value, box)
    return True


callback = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)(visit)
u.EnumWindows(callback, 0)
if args.click:
    handle, x, y = args.click
    if handle not in windows:
        raise SystemExit('Click target is not a visible window of the verified sandbox JVM')
    _, box = windows[handle]
    if not 0 <= x < box[2] - box[0] or not 0 <= y < box[3] - box[1]:
        raise SystemExit('Relative click falls outside the verified sandbox window')
    u.SetForegroundWindow(handle)
    foreground_owner = wintypes.DWORD()
    u.GetWindowThreadProcessId(u.GetForegroundWindow(), ctypes.byref(foreground_owner))
    if foreground_owner.value != pid:
        raise SystemExit('Foreground belongs to another application; no mouse input sent')
    point = wintypes.POINT(box[0] + x, box[1] + y)
    hit_owner = wintypes.DWORD()
    u.GetWindowThreadProcessId(u.WindowFromPoint(point), ctypes.byref(hit_owner))
    if hit_owner.value != pid:
        raise SystemExit('Click point is covered by another application; no mouse input sent')
    u.SetCursorPos(point.x, point.y)
    u.mouse_event(2, 0, 0, 0, 0)
    u.mouse_event(4, 0, 0, 0, 0)
    print('Clicked verified sandbox window', handle, 'relative', x, y)
elif args.close:
    if args.close not in windows:
        raise SystemExit('Close target is not a visible owned sandbox window')
    if not u.PostMessageW(args.close, 0x0010, 0, 0):
        raise ctypes.WinError()
    print('Requested graceful WM_CLOSE for owned sandbox window; process exit must be read back', args.close)
else:
    from PIL import ImageGrab
    output = ROOT / '.scratch/issue-32'
    for handle, (title, box) in windows.items():
        path = output / f'sandbox-window-{handle}.png'
        ImageGrab.grab(bbox=box).save(path)
        print(json.dumps({'hwnd': handle, 'title': title, 'box': box, 'image': str(path)}, ensure_ascii=False))
