<p align="center">
  <a href="#" target="_blank">
    <img src="Resources/Images/AppIcon/SystemProcess.png" width="200" alt="Project Logo">
  </a>
</p>

# User Guide - System Processes

**System Processes** is a high-performance, lightweight system monitor and task manager designed for Windows. It provides a detailed, hierarchical view of running processes, real-time resource usage statistics, and advanced management tools—all while consuming minimal system resources itself.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [The Dashboard](#the-dashboard)
3. [Managing Processes](#managing-processes)
4. [Advanced Features](#advanced-features)
5. [System Tray Integration](#system-tray-integration)
6. [Troubleshooting](#troubleshooting)

---

## Getting Started

### Installation
System Processes is a portable application. No installation is required.
1. Download the latest release zip file.
2. Extract the contents to a folder of your choice.
3. Run `SystemProcesses.Desktop.exe`.

> **Note:** For full functionality (such as viewing details of system services or terminating privileged processes), it is recommended to **Run as Administrator**.

### Navigation
The main window is divided into three sections:
1. **Toolbar:** Action buttons for managing selected processes.
2. **Status Bar:** Real-time global system statistics (CPU, RAM, Disk).
3. **Process Tree:** A hierarchical list of all running applications and background services.

---

## The Dashboard

The status bar at the top of the window provides an instant overview of your computer's health.

| Metric | Description |
| :--- | :--- |
| **CPU** | Total processor usage percentage across all cores. |
| **RAM** | Physical memory usage. Shows available RAM and free percentage. |
| **VM** | Virtual Memory (Commit Limit). Shows available commit space and free percentage. |
| **Disk** | Active disk time percentage (indicates drive load). |
| **Storage** | Free space and usage percentage for all fixed drives (e.g., C:, D:). |
| **Counts** | Hover over the stats area to see total **Processes**, **Threads**, and **Handles**. |

---

## Managing Processes

You can interact with any process by selecting it in the list and using the **Toolbar** buttons or **Right-Clicking** the item.

### Terminating Processes
System Processes offers two distinct ways to stop an application:

#### 1. Graceful End (Recommended)
*   **Action:** Sends a "Close" request to the application's main window.
*   **Behavior:** This is equivalent to clicking the **X** button on a window. It gives the application a chance to save data and exit cleanly.
*   **Tree Variant:** **Graceful End Tree** attempts to close the selected process and all its children safely.

#### 2. End (Force Kill)
*   **Action:** Immediately terminates the process.
*   **Behavior:** The application stops instantly. Unsaved data may be lost. Use this if a program is frozen or "Not Responding."
*   **Tree Variant:** **End Tree** forcefully kills the selected process and every child process spawned by it.

### Other Actions
*   **Details:** Opens a dialog showing extended information (Start Time, Command Line arguments, full file path).
*   **Open Directory:** Opens the folder containing the process executable in Windows Explorer.
*   **Copy Path:** (Right-click only) Copies the full file path of the executable to your clipboard.

---

## Advanced Features

### 🌳 Process Tree View
Processes are shown in a parent-child hierarchy.
*   **Expand/Collapse:** Click the arrow next to a process to see what sub-processes it has created.
*   **Columns:**
    *   **PID:** Process ID.
    *   **Name:** Executable name.
    *   **CPU:** Usage percentage for that specific process.
    *   **Memory:** Working set (physical RAM) used.
    *   **VM:** Virtual memory size.
    *   **Parameters:** Command line arguments used to start the process.

### 🔍 Search & Filter
*   **Search Bar:** Type in the top-right box to filter the list. You can search by **Process Name** or **PID**.
*   **Behavior:** When searching, the tree structure is preserved so you can still see the context of the matched items.

### 🛡️ Tree Isolation
Focus on a specific application group without the noise of the rest of the system.
1. Select a process (e.g., a web browser with many tabs).
2. Click **Isolate Tree** in the toolbar or context menu.
3. The view will filter to show **only** that process and its descendants.
4. Toggle the button off to return to the full system view.

### ⏱️ Refresh Rate
Use the dropdown menu in the top-right corner to change how often data updates:
*   **Fast (1s - 2s):** Best for real-time monitoring.
*   **Slow (5s - 20s):** Reduces the application's own resource usage.
*   **Disabled:** Pauses updates completely (useful when inspecting a rapidly changing list).

---

## System Tray Integration

When minimized, System Processes stays active in the system tray area (near the clock).

*   **Dynamic Icon:** The tray icon changes to show the current **CPU Usage number** (0-99), giving you a health check at a glance.
*   **Smart Tooltip:** Hover over the icon to see:
    *   Summary of CPU, RAM, VM, and Disk usage.
    *   **Top 5 Processes:** A live list of the top 5 apps currently using the most CPU.
*   **Context Menu:** Right-click the tray icon to quickly **Show** the window or **Exit** the application completely.

---

## Troubleshooting

### "Access Denied" or Missing Details
*   **Cause:** Windows security prevents standard users from inspecting system-level processes or services.
*   **Solution:** Restart the application as an Administrator. Right-click `SystemProcesses.Desktop.exe` and select **Run as administrator**.

### Application Not Closing via "Graceful End"
*   **Cause:** The application might be frozen, waiting for user input (like a "Save Changes" dialog), or it might be a background process with no visible window.
*   **Solution:** If Graceful End fails, use the **End** (Force Kill) button.

### High CPU Usage by System Processes App
*   **Cause:** Very low refresh intervals (e.g., 1s) on older hardware can cause slight CPU usage due to the speed of data collection.
*   **Solution:** Set the Refresh Interval to **2** or **5** seconds.
