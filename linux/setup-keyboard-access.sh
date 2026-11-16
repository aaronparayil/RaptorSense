#!/bin/bash
# Lets you, and not only root, set the keyboard's lighting. Asks for your password once (sudo).
# It installs one udev rule that opens the keyboard's lighting interface, and nothing that carries what you type.
# To undo it:  sudo rm /etc/udev/rules.d/70-raptorsense.rules && sudo udevadm control --reload
set -e
here="$(cd "$(dirname "$0")" && pwd)"

sudo install -m 644 "$here/70-raptorsense.rules" /etc/udev/rules.d/70-raptorsense.rules
sudo udevadm control --reload
sudo udevadm trigger --subsystem-match=hidraw
sleep 1

echo "Rule installed. The keyboard's interfaces now look like this (the lighting one should show a + or your name):"
for dev in /sys/class/hidraw/hidraw*; do
  if grep -qiE 'HID_ID=0003:000004F2:0000011[79A]' "$dev/device/uevent" 2>/dev/null; then
    ls -l "/dev/$(basename "$dev")"
  fi
done
