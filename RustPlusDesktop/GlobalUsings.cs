// Achievement triggers live wherever the thing they track actually happens, which
// is most files in the app. A global using keeps each of them to a single line
// rather than an extra import in thirty files.
global using Ach = RustPlusDesk.Services.Achievements.Ach;
