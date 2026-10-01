 import os
# import cv2
# import pandas as pd
# import numpy as np
# from datetime import datetime
# from pyzbar.pyzbar import decode
# import fitz
# import tkinter as tk
# from tkinter import filedialog, messagebox
# import sys
# import time
# import threading
# from PIL import Image, ImageTk



# # ---------------- RESOURCE PATH FUNCTION ---------------- #
# def resource_path(relative_path):
#     try:
#         base_path = sys._MEIPASS   # PyInstaller temp folder
#     except Exception:
#         base_path = os.path.abspath(".")
#     return os.path.join(base_path, relative_path)

# sys.stderr = open(os.devnull, 'w')

# # ---------------- GLOBAL VARIABLES ---------------- #
# submit_btn = None
# start_time = None
# processing_done = True
# cancel_requested = False
# total_files_global = 0
# processed_count_global = 0

# # ---------------- DIGITAL TIME FORMAT ---------------- #
# def digital_time(seconds):
#     seconds = int(seconds)
#     hrs = seconds // 3600
#     mins = (seconds % 3600) // 60
#     secs = seconds % 60
#     return f"{hrs:02}h {mins:02}m {secs:02}s"

# # ---------------- TIMER UPDATE FUNCTION ---------------- #
# def update_timer():
#     global start_time, processing_done
#     global total_files_global, processed_count_global

#     if start_time and not processing_done:

#         elapsed = time.time() - start_time

#         if processed_count_global > 0:
#             avg = elapsed / processed_count_global
#             remaining = avg * (total_files_global - processed_count_global)
#         else:
#             remaining = 0

#         timer_label.config(
#             text=f"Completed: {digital_time(elapsed)}\n"
#                  f"Remaining: {digital_time(remaining)}"
#         )

#         root.after(1000, update_timer)

# class PDFBarcodeVerifier:
#     cancel_requested = False

#     def pdf_to_images(self, pdf_path):
#         images = []
#         try:
#             doc = fitz.open(pdf_path)
#             for page in doc:
#                 pix = page.get_pixmap(dpi=300)
#                 img = np.frombuffer(pix.samples, dtype=np.uint8)
#                 img = img.reshape(pix.height, pix.width, pix.n)

#                 if pix.n == 4:
#                     img = cv2.cvtColor(img, cv2.COLOR_RGBA2BGR)
#                 else:
#                     img = cv2.cvtColor(img, cv2.COLOR_RGB2BGR)

#                 images.append(img)
#             return images
#         except:
#             return []

#     def extract_barcode(self, image):
#         try:
#             gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
#             enhanced = cv2.convertScaleAbs(gray, alpha=1.5, beta=0)
#             barcodes = decode(enhanced)
#             if barcodes:
#                 return barcodes[0].data.decode("utf-8").strip()
#             return None
#         except:
#             return None

#     def process_folder(self, folder_path, expected_pages):

#         global processing_done
#         global total_files_global, processed_count_global

#         pdf_files = [f for f in os.listdir(folder_path)
#                      if f.lower().endswith(".pdf")]

#         if not pdf_files:
#             messagebox.showerror("Error", "No PDF files found!")
#             return None

#         total_files_global = len(pdf_files)
#         processed_count_global = 0

#         results = []
#         sr_no = 1

#         for pdf_file in pdf_files:
#             if cancel_requested:
#                 break

#             processed_count_global += 1

#             # Update status safely from thread
#             root.after(0, lambda f=pdf_file:
#                        status_label.config(
#                            text=f"Processing {processed_count_global}/{total_files_global} : {f}"
#                        ))

#             pdf_path = os.path.join(folder_path, pdf_file)
#             images = self.pdf_to_images(pdf_path)
#             total_pages_found = len(images)

#             if total_pages_found == 0:
#                 continue

#             reference_barcode = self.extract_barcode(images[0])

#             mismatch_pages = []
#             mismatch_details = []
#             missing_pages = []

#             for i in range(0, total_pages_found, 2):
#                 page_number = i + 1
#                 barcode = self.extract_barcode(images[i])
#                 if barcode and reference_barcode and barcode != reference_barcode:
#                     mismatch_pages.append(str(page_number))
#                     mismatch_details.append(f"Page {page_number}: {barcode}")

#             expected_odd_pages = list(range(1, expected_pages + 1, 2))

#             for page in expected_odd_pages:
#                 if page > total_pages_found:
#                     missing_pages.append(str(page))

#             if not mismatch_pages and not missing_pages:
#                 final_status = "VALID"
#             elif missing_pages:
#                 final_status = "INVALID (Pages Count not Match)"
#             elif mismatch_pages:
#                 final_status = "INVALID (Barcode Mismatch)"
#             else:
#                 final_status = "INVALID"

#             results.append({
#                 "Sr No": sr_no,
#                 "Folder Name": os.path.basename(folder_path),
#                 "File Name": pdf_file,
#                 "Total PDFs in Folder": total_files_global,
#                 "Expected Pages": expected_pages,
#                 "Total Pages Found": total_pages_found,
#                 "Reference Barcode": reference_barcode if reference_barcode else "-",
#                 "Mismatch Pages": ", ".join(mismatch_pages) if mismatch_pages else "-",
#                 "Mismatch Details": ", ".join(mismatch_details) if mismatch_details else "-",
#                 "Missing Pages Count": expected_pages - total_pages_found,
#                 "Final Status": final_status,
#                 "Processed Time": datetime.now().strftime("%d-%m-%Y %H:%M:%S")
#             })

#             sr_no += 1

#         processing_done = True
        
#         root.after(0, lambda: submit_btn.config(text="Submit", bg="#00457c"))
        
#         if cancel_requested:
#             root.after(0, lambda:
#             status_label.config(text="Process Cancelled ❌"))

#         total_elapsed = time.time() - start_time

#         root.after(0, lambda:
#                    status_label.config(
#                        text=f"Completed ✅ | Total PDFs Processed: {processed_count_global}of {total_files_global}"
#                    ))

#         root.after(0, lambda:
#                    timer_label.config(
#                        text=f"Total Time: {digital_time(total_elapsed)}\nRemaining: 00h 00m 00s"
#                    ))

#         timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
#         output_file = os.path.join(
#             folder_path,
#             f"barcode_report_{timestamp}.csv"
#         )

#         df = pd.DataFrame(results)
#         df.to_csv(output_file, index=False)

#         return output_file


# # ---------------- GUI FUNCTIONS ---------------- #

# def select_folder():
#     folder_path = filedialog.askdirectory()
#     if folder_path:
#         folder_path_var.set(folder_path)

#         pdf_files = [f for f in os.listdir(folder_path)
#                      if f.lower().endswith(".pdf")]

#         selected_folder_label.config(
#             text=f"Selected Folder: {os.path.basename(folder_path)} | "
#                  f"Total PDFs: {len(pdf_files)}"
#         )

# def run_processing(folder_path, expected_pages):
#     global processing_done

#     verifier = PDFBarcodeVerifier()
#     output = verifier.process_folder(folder_path, expected_pages)

#     processing_done = True

#     if output:
#         root.after(0, lambda:
#                    messagebox.showinfo("Success", f"Report Generated:\n{output}"))

# def submit_action():
#     global start_time, processing_done, cancel_requested

#     # If already processing → Cancel
#     if not processing_done:
#         cancel_requested = True
#         status_label.config(text="Cancelling... ❌")
#         return

#     folder_path = folder_path_var.get()

#     if not folder_path:
#         messagebox.showerror("Error", "Select folder first!")
#         return

#     try:
#         expected_pages = int(expected_pages_var.get())
#     except:
#         messagebox.showerror("Error", "Enter valid expected page count!")
#         return

#     # Start Process
#     cancel_requested = False
#     processing_done = False
#     start_time = time.time()

#     submit_btn.config(text="Cancel", bg="red")

#     status_label.config(text="Starting validation...")
#     timer_label.config(text="Completed: 00h 00m 00s\nRemaining: 00h 00m 00s")

#     update_timer()

#     thread = threading.Thread(
#         target=run_processing,
#         args=(folder_path, expected_pages)
#     )
#     thread.start()
# # ---------------- MAIN WINDOW ---------------- #

# root = tk.Tk()
# try:
#     root.iconbitmap(resource_path("icon.ico"))
# except:
#     pass
# root.state("zoomed")
# root.configure(bg="#ffffff")
# root.title("AUS | Examcell PDF Barcode Validator")

# folder_path_var = tk.StringVar()
# expected_pages_var = tk.StringVar()

# logo_image = Image.open(resource_path("logo.png"))
# logo_image = logo_image.resize((750, 95))
# logo_photo = ImageTk.PhotoImage(logo_image)

# tk.Label(root, image=logo_photo, bg="#ffffff").pack()

# tk.Label(root,
#          text="Batch PDF Barcode Validator",
#          font=("Poppins", 20, "bold"),
#          bg="#ffffff",
#          fg="#00457c").pack(pady=15)

# tk.Button(root,
#           text="Select Folder",
#           command=select_folder,
#           bg="#00457c",
#           fg="white",
#           font=("Poppins", 13, "bold"),
#           bd=0,
#           padx=30,
#           pady=2).pack(pady=5)

# selected_folder_label = tk.Label(root,
#                                  text="No folder selected",
#                                  font=("Poppins", 10),
#                                  bg="#ffffff",
#                                  fg="#555555")
# selected_folder_label.pack()

# tk.Label(root,
#          text="Enter Expected Page Count:",
#          font=("Poppins", 11),
#          bg="#ffffff").pack(pady=5)

# tk.Entry(root,
#          textvariable=expected_pages_var,
#          width=15,
#          font=("Poppins", 11),
#          bg="white",
#          relief="solid",
#          bd=2).pack()

# submit_btn = tk.Button(root,
#           text="Submit",
#           command=submit_action,
#           bg="#00457c",
#           fg="white",
#           font=("Poppins", 13, "bold"),
#           bd=0,
#           padx=20,
#           pady=2)

# submit_btn.pack(pady=15)

# status_label = tk.Label(root,
#                         text="Status: Waiting for folder upload...",
#                         bg="#ffffff",
#                         fg="blue",
#                         font=("Poppins", 11))
# status_label.pack(pady=5)

# timer_label = tk.Label(root,
#                        text="Completed: 00h 00m 00s\nRemaining: 00h 00m 00s",
#                        bg="#ffffff",
#                        fg="green",
#                        font=("Poppins", 11, "bold"))
# timer_label.pack()

# tk.Label(root,
#          text="Developed by IT Applications",
#          font=("Poppins", 10),
#          bg="#ffffff",
#          fg="#929090").pack(side="bottom", pady=15)

# root.mainloop()