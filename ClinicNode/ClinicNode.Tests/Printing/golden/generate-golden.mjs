// JJQR - AI (Nov 21, 2026) - writes forms.json / rxsig.json from the REAL web builders (Project02-fe print-forms-html.ts, rx-sig-parse.ts).
// The C# tests feed the same inputs to ClinicNode.Core.Printing and require byte-identical HTML.
// Usage (from this folder):  node generate-golden.mjs
import { writeFileSync } from "node:fs";
const FE = new URL("../../../../../Project02-fe/src/lib/", import.meta.url);
const { buildLabRequestHtml, buildMedicalCertificateHtml, buildPrescriptionHtml } = await import(new URL("print-forms-html.ts", FE));
const { parseRxSig, rxDisplayName } = await import(new URL("rx-sig-parse.ts", FE));

const ORIGIN = "http://forms.test";
const doctor = { full_name: "Jane Doe", license_number: "LIC-99", ptr_number: "PTR-88", s2_number: "S2-77" };
const doctorNoS2 = { full_name: "Allyn Grace T. España-Gavino, MD", license_number: "0123456", ptr_number: null };
const doctorLong = { full_name: "Maria Concepcion Bartolome-Villanueva de los Santos MD", license_number: "1", ptr_number: "2" };
const doctorNone = { full_name: "", license_number: null, ptr_number: null };
const patient = { full_name: "Juan Dela Cruz", sex: "Male", address: "Buaya, Lapu Lapu City" };
const patientF = { full_name: "Ana <Reyes> & \"Co\"", sex: "Female", address: "residing at Pusok, , Lapu-Lapu" };

const meds = [
  { generic_name: "Paracetamol", dosage: "500 mg", quantity: "20 tablets", instruction: "as needed", timing: "Breakfast,Dinner", meal_relation: "After", duration_kind: "Days", duration_value: 5, indication: "for fever", route: "PO" },
  { generic_name: "Amoxicillin 500mg capsule", dosage: "500mg", quantity: "21", instruction: "1 cap TID after meals", timing: null, meal_relation: null, duration_kind: "Weeks", duration_value: 1, indication: "infection" },
  { generic_name: "Losartan", dosage: "50 mg", quantity: "30", instruction: "once daily at night", duration_kind: "Maintain", duration_value: null, indication: null, route: "Oral" },
  { generic_name: "ORS SACHET", dosage: "1 sachet", quantity: "10", instruction: "after each loose stool", as_needed: true },
  { generic_name: "Salbutamol", dosage: "2 mg", quantity: "15", instruction: "take take one tablet every 8 hours with food for cough and wheezing, continue until symptoms resolve completely please", route: "take", as_needed: false },
  { generic_name: "Cetirizine", dosage: "10mg", quantity: "7", instruction: "as directed by physician" },
];

const cases = [];
const add = (kind, input, html) => cases.push({ kind, input, html });
const rx = (o) => add("rx", o, buildPrescriptionHtml(o));
const mc = (o) => add("mc", o, buildMedicalCertificateHtml(o));
const lab = (o) => add("lab", o, buildLabRequestHtml(o));

rx({ origin: ORIGIN, pageSize: "148mm 210mm", doctor, patient, age: "34", date: "November 21, 2026", items: meds.slice(0, 1), nextAppointment: "" });
rx({ origin: "", pageSize: "148mm 210mm", doctor: doctorNoS2, patient: patientF, age: "", date: "January 2, 2026", items: meds.slice(0, 5), nextAppointment: "December 1, 2026" });
rx({ origin: "", pageSize: "148mm 210mm", doctor: doctorNone, patient, age: "7", date: "March 3, 2026", items: meds });
mc({ origin: ORIGIN, pageSize: "148mm 210mm", doctor, patient, issueDate: "November 21, 2026", address: "Buaya, Lapu Lapu City", examinedAt: "Grace Medical Clinic", examFrom: "November 20, 2026", examTo: "November 21, 2026", diagnosis: "Acute   upper respiratory\ninfection", recommendations: "Rest for 3 days", purposeException: "", comeBackOn: "November 28, 2026" });
mc({ origin: "", pageSize: "148mm 210mm", doctor: doctorNoS2, patient: patientF, issueDate: "June 1, 2026", address: "Pusok, Lapu-Lapu", examinedAt: "", examFrom: "", examTo: "", diagnosis: "", recommendations: "", purposeException: "  legal proceedings  ", comeBackOn: "" });
lab({ origin: ORIGIN, pageSize: "4.3in 6.4in", doctor, patient, age: "34", date: "November 21, 2026", tests: ["CBC", "Urine test", "Lipid panel", "Serum creatinine", "Dengue NS1", "ECG", "HbA1c", "Chest X-ray PA"], diagnoses: ["J06.9 — Acute URTI", "E11"], clinicalNotes: "  r/o dengue  " });
lab({ origin: "", pageSize: "4.3in 6.4in", doctor: doctorLong, patient: patientF, age: "", date: "", tests: ["FBS", "sgpt", "BUA"] });
lab({ origin: "", pageSize: "4.3in 6.4in", doctor: doctorNone, patient, age: "1", date: "May 5, 2026", tests: ["Thyroid <panel>"], diagnoses: [], clinicalNotes: "" });
writeFileSync(new URL("forms.json", import.meta.url), JSON.stringify(cases, null, 1));

const sigs = ["after meals", "before meals", "once daily", "BID", "TID with meals", "1 tab q8h", "every 6 hours", "at bedtime", "once daily at night",
  "prn for fever", "for pain", "after each loose stool", "stat", "as directed", "morning and evening", "1 hour before breakfast", "a.c.", "p.c. tid",
  "twice a day with food", "umaga at tanghali", "8am", "with food", "empty stomach", "", "take 2 puffs"];
const structured = [
  { instruction: "after meals", mealRelation: "Before" }, { instruction: "once daily", mealRelation: "AFTER" },
  { instruction: "tid", timing: "Lunch, Bedtime" }, { instruction: "", route: "PO", timing: "breakfast" },
];
const sigCases = sigs.map((s) => ({ input: { instruction: s }, result: parseRxSig({ instruction: s }) }))
  .concat(structured.map((i) => ({ input: i, result: parseRxSig(i) })));
const names = [["Paracetamol", "500 mg"], ["Paracetamol 500mg tablet", "500 mg"], ["Amoxicillin 500MG", "500mg/5ml suspension"], ["ORS SACHET", "1 sachet"],
  ["Cefalexin 250mg/5ml", "250 mg/5 ml"], ["Losartan", ""], ["Metformin 500MG TAB", "1 tab"], ["Salbutamol", "2 mg tablet"]];
const nameCases = names.map(([g, d]) => ({ generic: g, dosage: d, result: rxDisplayName(g, d) }));
writeFileSync(new URL("rxsig.json", import.meta.url), JSON.stringify({ sigCases, nameCases }, null, 1));
console.log(`forms.json: ${cases.length} cases, rxsig.json: ${sigCases.length} sig + ${nameCases.length} name cases`);
// JJQR - END
