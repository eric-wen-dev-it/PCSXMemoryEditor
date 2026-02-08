# PCSXMemoryEditor

**A high-performance memory manipulation utility for the PCSX2 emulator.**

### **Project Overview**

**PCSXMemoryEditor** is a specialized tool designed to interact with the Emotion Engine (EE) RAM of the PlayStation 2 emulator, PCSX2. It provides a modern, responsive interface for real-time memory scanning, history tracking, and value "freezing."

The core architecture—including its high-efficiency scanning algorithms, thread-safe memory synchronization, and MVVM-patterned UI—was developed through **collaborative coding with Gemini**. By leveraging advanced prompt engineering, the project implements modern C# 12 best practices and optimized data structures.

---

### **Key Features**

* **Optimized Scanning**: Rapidly searches the 32MB EE memory space using buffer-based I/O and `Parallel.ForEach` processing.
* **Sliding Window History**: Tracks value changes across ten stages (H0–H9), allowing for easy visualization of data trends as they happen in-game.
* **High-Frequency Locking**: A robust "Freeze" system that enforces values in the emulator's RAM every 100ms via a dedicated synchronization loop.
* **Modern UI Engine**: Built with WPF and **UI Virtualization**, enabling the display of thousands of results without performance degradation.
* **Hex Editor Integration**: Direct memory inspection and one-click address locking from a built-in Hex view.

---

### **How to Use**

#### **1. Connection**

Launch **PCSX2** and load your game. Open the editor and click **Connect**. The tool will attach to the process and map the EE memory space.

#### **2. Searching for Values**

* **First Scan**: Select your **Data Type** (4-byte, Float, etc.), enter your current in-game value (like Gold or HP), and click **First Scan**.
* **Next Scan**: Change the value in-game, enter the new number, and click **Next Scan** to filter the results. Repeat until you find the specific address.

#### **3. Using History (H0–H9)**

The results grid displays a real-time history. Every time a value changes, the old value shifts to the right (H0 becomes H1, etc.). This is perfect for identifying values that change exactly when you perform a specific action.

#### **4. Locking (Freezing) Values**

Click the **Lock** button in the grid or **Double-Click** an address in the Hex Editor. The address moves to the **Locked Items** list, where the tool will continuously force that value back into the game memory.

#### **5. Managing Locks**

**Right-click** any item in the **Locked List** to remove it or clear the entire list. The list will automatically scroll to and highlight the most recently added items.

---

### **Technical Implementation (Developed with Gemini)**

This project serves as a showcase of human-AI synergy in software development:

* **C# 12 Collection Expressions**: Clean handling of data using `[.. spread]` operators.
* **Efficiency**:  lookups via `SortedSet` and `HashSet` to ensure the UI stays responsive even with 50,000+ scan results.
* **Asynchronous Flow**: Full implementation of `async/await` and `Task.Run` to prevent UI "not responding" states during heavy memory I/O.
* **Thread Safety**: Precise use of `lock` mechanisms and `Dispatcher` marshaling for stable communication between the game-reading tasks and the user interface.

---

### **Project Metadata**

* **Framework**: .NET 8 / WPF (Windows)
* **Language**: C# 12
* **Development Partner**: Gemini AI (Prompt-guided Architecture)
