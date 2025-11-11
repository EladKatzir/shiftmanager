# “הצוות שלי” — מחרוזות ותוויות בעברית (Copy‑Ready)
> כל המחרוזות לממשק משתמש. מומלץ למפות למפתחות i18n כמוצע.

---

## גלובלי
| key | he |
|---|---|
| app.title | הצוות שלי |
| actions.save | שמור |
| actions.cancel | בטל |
| actions.close | סגור |
| actions.edit | ערוך |
| actions.delete | מחק |
| actions.confirm | אישור |
| actions.create | יצירה |
| actions.rename | שינוי שם |
| actions.search | חיפוש |
| actions.back | חזרה |
| actions.next | הבא |
| actions.previous | הקודם |
| actions.ok | אישור |
| actions.try_again | נסו שוב |
| actions.clear | נקה |
| labels.loading | טוען… |
| labels.error_generic | אירעה שגיאה |

---

## תצוגת שבוע ראשית
| key | he |
|---|---|
| week.header.title | {calendarName} |
| week.header.configure | הגדרת חברי צוות |
| week.header.switch | החלפת לוח |
| week.header.new | לוח צוות חדש |
| week.nav.prev | שבוע קודם |
| week.nav.this | השבוע |
| week.range.format | {startDayShort} {startDate}–{endDayShort} {endDate} {monthYear} |
| week.table.member_column | חבר צוות |
| week.empty.title | אין חברים בלוח זה |
| week.empty.body | הוסיפו חברים כדי להתחיל לראות את מצב הצוות בשבוע |
| week.empty.cta | הגדרת חברי צוות |

### ימי השבוע
| key | he (מלא) | he (מקוצר) |
|---|---|---|
| day.sunday | יום ראשון | א׳ |
| day.monday | יום שני | ב׳ |
| day.tuesday | יום שלישי | ג׳ |
| day.wednesday | יום רביעי | ד׳ |
| day.thursday | יום חמישי | ה׳ |
| day.friday | יום שישי | ו׳ |
| day.saturday | שבת | ש׳ |

---

## סטטוס בתא (עדיפות: חופשה → אפטר → כוננות → משמרת → מטלה → פנוי)
| key | he |
|---|---|
| status.vacation | חופשה |
| status.vacation_until_1pm | חופשה עד 13:00 |
| status.after_from_4pm | אפטר מ-16:00 |
| status.after_until_1pm | אפטר עד 13:00 |
| status.onduty | כוננות |
| status.shift | משמרת |
| status.shift_named | משמרת {name} |
| status.chore | מטלה |
| status.chore_named | מטלה {name} |
| status.chore_plus_n | {firstName} +{extraCount} |
| status.free | פנוי |

### טולטיפים בתא
| key | he |
|---|---|
| cell.tooltip.opens | ייפתח: {targetPage} |
| cell.tooltip.time_range | {start} – {end} |
| cell.tooltip.more | פרטים נוספים |

### כותרות דפים לטולטיפים
| key | he |
|---|---|
| pages.calendar_table | לוח השנה (טבלה) |
| pages.chores | מטלות |
| pages.onduty | כוננות |
| pages.timeoff | חופשות |

---

## מחליף לוחות (DDL מהכפתור ⟳)
| key | he |
|---|---|
| ddl.title | הלוחות שלי |
| ddl.switch | בחר |
| ddl.rename | שנה שם |
| ddl.delete | מחק |
| ddl.create_new | צור לוח חדש |
| ddl.search.placeholder | חיפוש לוח… |
| ddl.delete.confirm.title | למחוק את "{name}"? |
| ddl.delete.confirm.body | המחיקה תסיר רק את התצוגה האישית שלך. אין השפעה על הנתונים. |
| ddl.delete.confirm.confirm | מחק |
| ddl.delete.confirm.cancel | בטל |
| ddl.rename.placeholder | שם לוח חדש… |
| ddl.rename.error.empty | נדרש שם ללוח |
| ddl.rename.error.duplicate | כבר קיים לוח בשם זה |
| ddl.rename.error.length | השם חייב להיות באורך 1–60 תווים |

---

## יצירת לוח חדש / שינוי שם
| key | he |
|---|---|
| create.title | לוח "הצוות שלי" חדש |
| create.name.label | שם הלוח |
| create.name.placeholder | למשל: צוות פרויקט X |
| create.buttons.create | יצירה |
| create.buttons.cancel | ביטול |
| rename.inline.help | הקלידו שם חדש ולחצו Enter לשמירה או Esc לביטול |

---

## מנהל חברים (גלגל שיניים)
| key | he |
|---|---|
| members.title | חברי צוות |
| members.left.title | חברי הצוות הנוכחיים |
| members.right.title | משתמשי החברה שלא בצוות |
| members.search.left | חיפוש לפי שם… |
| members.search.right | חיפוש לפי שם… |
| members.count.left | חברים בלוח: {count} |
| members.count.right | לא בצוות: {count} |
| members.actions.save | שמור שינויים |
| members.actions.cancel | בטל |
| members.actions.add_selected | הוסף נבחרים |
| members.actions.remove_selected | הסר נבחרים |
| members.empty.left | אין עדיין חברים בלוח |
| members.empty.right | לא נמצאו משתמשים |

### הנגשה (Labels/ARIA)
| key | he |
|---|---|
| a11y.open_member_manager | פתח חלון הגדרת חברי צוות |
| a11y.open_calendar_switcher | פתח רשימת הלוחות |
| a11y.create_calendar | צור לוח צוות חדש |
| a11y.week_grid | טבלת שבוע של הצוות שלי |
| a11y.member_row | שורת חבר צוות: {name} |
| a11y.day_cell | תא ליום {day} |
| a11y.status_label | סטטוס: {status} |
| a11y.remove_member | הסר את {name} מהצוות |
| a11y.add_member | הוסף את {name} לצוות |

---

## מצבי ריק/טעינה/שגיאה
| key | he |
|---|---|
| empty.no_members.title | לא נבחרו חברים |
| empty.no_members.body | לחצו על "הגדרת חברי צוות" כדי להוסיף חברים ללוח |
| loading.grid | טוען את מצב השבוע… |
| error.load_grid | לא הצלחנו לטעון את מצב השבוע |
| error.load_members | לא הצלחנו לטעון את רשימות המשתמשים |

---

## נייד/מסכים קטנים
| key | he |
|---|---|
| mobile.menu.open | תפריט |
| mobile.menu.configure | הגדרת חברי צוות |
| mobile.menu.switch | החלפת לוח |
| mobile.menu.new | לוח צוות חדש |
| mobile.more | עוד |

---

## פורמטים ותצוגת שעה
| key | he |
|---|---|
| time.at | בשעה {time} |
| time.from | מ-{time} |
| time.until | עד {time} |
| time.1pm | 13:00 |
| time.4pm | 16:00 |

---

## הודעות אימות נוספות
| key | he |
|---|---|
| validation.required | שדה חובה |
| validation.min_length | קצר מדי |
| validation.max_length | ארוך מדי |

---

## טקסטים נוספים לתאים (אופציונלי)
| key | he |
|---|---|
| cell.multiple_items | ועוד {count} פריטים |
| cell.free.tooltip | אין משימות ליום זה |

---

### הערות
- “אפתר” הוא שם סוג החופשה כפי שהוגדר (ללא ניקוד).
- אנא הגדירו RTL בממשק כדי להצמיד את הטבלאות והטקסט לימין.

