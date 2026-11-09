using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What money came in or went out for.</summary>
public enum MoneyCategory
{
    /// <summary>Payment for each A&amp;E attendance, by how sick the patient was.</summary>
    Attendances,

    /// <summary>Payment for each patient admitted to a ward, plus each night they stay.</summary>
    Admissions,

    /// <summary>Wages for every hire on the day and night teams.</summary>
    Staff,

    Tests,

    Medicines,

    /// <summary>Cleaning, heating, lighting and repairs, by floor area.</summary>
    Upkeep,

    /// <summary>Building, moving and demolishing rooms and extending the building.</summary>
    Construction,

    /// <summary>Payment for each operation done in theatre.</summary>
    Operations,

    /// <summary>Payment for each outpatient seen in clinic.</summary>
    Clinics,

    /// <summary>The hospital's share of what patients spend in its cafes and vending machines.</summary>
    Catering,

    /// <summary>Money added from the cheat console.</summary>
    Cheats,

    /// <summary>Payment for each birth (more for a caesarean section) and each day a baby spends on the neonatal unit.</summary>
    Maternity,
}

/// <summary>Money in and out on one day, by category. Spending is negative.</summary>
public sealed class DayAccounts
{
    public required DateTime Date { get; init; }

    public Dictionary<MoneyCategory, long> Amounts { get; } = new();

    public long Income => Amounts.Values.Where(v => v > 0).Sum();

    public long Spending => Amounts.Values.Where(v => v < 0).Sum();

    public long Net => Amounts.Values.Sum();

    public long this[MoneyCategory category] => Amounts.GetValueOrDefault(category);

    internal void Add(MoneyCategory category, long amount) => Amounts[category] = Amounts.GetValueOrDefault(category) + amount;
}

/// <summary>
/// The hospital's money: payments for patients seen and admitted, and the running costs of staff,
/// tests, medicines and the building. Payments are a simplified version of the NHS tariff.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Payment for an A&amp;E attendance by triage category.</summary>
    public static readonly IReadOnlyDictionary<TriageCategory, int> AttendanceTariff = new Dictionary<TriageCategory, int>
    {
        [TriageCategory.Red] = 450,
        [TriageCategory.Amber] = 250,
        [TriageCategory.Green] = 130,
    };

    /// <summary>Payment when an admitted patient goes home, plus <see cref="BedDayTariff"/> for each night on the ward.</summary>
    public const int AdmissionTariff = 1_200;

    public const int BedDayTariff = 300;

    /// <summary>Payment for an operation, on top of the admission.</summary>
    public const int OperationTariff = 2_500;

    /// <summary>Without a pharmacy on site, medicines are bought in from outside at this mark-up.</summary>
    public const double NoPharmacyMedicineMarkup = 1.5;

    /// <summary>
    /// Each hire covers one 12-hour shift (days or nights) every day of the week: about 2.25 people. So a
    /// day hire and a night hire together cost what a round-the-clock post did before shifts.
    /// </summary>
    public const double StaffPerPost = 2.25;

    /// <summary>Upkeep per square metre of building per day.</summary>
    public const double UpkeepPerSquareMetrePerDay = 0.6;

    /// <summary>Days of accounts kept for the finance panel.</summary>
    private const int DaysKept = 14;

    private readonly List<DayAccounts> _days = new();
    private double _unpaidWages;
    private double _unpaidUpkeep;
    private bool _overdrawnWarned;

    /// <summary>Recent days' accounts, today first.</summary>
    public IReadOnlyList<DayAccounts> Accounts => _days;

    public DayAccounts Today => Day(Now.Date);

    /// <summary>What the day and night teams cost a day in wages.</summary>
    public long StaffCostPerDay => (long)Math.Round(_staff.Sum(s => s.Salary) * StaffPerPost / 365.0);

    public long UpkeepPerDay => (long)Math.Round(MainBuilding.AreaSquareMetres * UpkeepPerSquareMetrePerDay);

    private DayAccounts Day(DateTime date)
    {
        if (_days.Count > 0 && _days[0].Date == date)
        {
            return _days[0];
        }

        var day = new DayAccounts { Date = date };
        _days.Insert(0, day);
        if (_days.Count > DaysKept)
        {
            _days.RemoveAt(_days.Count - 1);
        }

        return day;
    }

    /// <summary>Records money in (positive) or out (negative) and updates the balance.</summary>
    /// <summary>
    /// Unlimited money: building, unlocking and hiring never stop for lack of money, so the balance can
    /// go into the red. Wages, income and the accounts are still counted as normal.
    /// </summary>
    public bool UnlimitedMoney { get; set; }

    /// <summary>Whether something costing <paramref name="cost"/> can be paid for now.</summary>
    public bool CanAfford(long cost) => UnlimitedMoney || cost <= 0 || Balance >= cost;

    internal void Book(MoneyCategory category, long amount)
    {
        if (amount == 0)
        {
            return;
        }

        Balance += amount;
        Day(Now.Date).Add(category, amount);
        Tally(StatKeys.Money(category), amount);

        if (Balance < 0 && !_overdrawnWarned && !UnlimitedMoney)
        {
            _overdrawnWarned = true;
            Notifications.Add(Now, NotificationLevel.Critical, $"The hospital is overdrawn (£{Balance:N0}). Building is stopped until money comes in. Look at Finances to see where it is going.");
        }
        else if (Balance > 0)
        {
            _overdrawnWarned = false;
        }
    }

    /// <summary>Wages and upkeep, charged a little every tick.</summary>
    private void ChargeRunningCosts(double dt)
    {
        _unpaidWages += StaffCostPerDay * dt / 86_400.0;
        _unpaidUpkeep += UpkeepPerDay * dt / 86_400.0;
        if (_unpaidWages >= 10)
        {
            var due = (long)Math.Floor(_unpaidWages);
            _unpaidWages -= due;
            Book(MoneyCategory.Staff, -due);
        }

        if (_unpaidUpkeep >= 10)
        {
            var due = (long)Math.Floor(_unpaidUpkeep);
            _unpaidUpkeep -= due;
            Book(MoneyCategory.Upkeep, -due);
        }
    }

    /// <summary>The A&amp;E attendance is paid once, when the patient leaves A&amp;E (home, ward, elsewhere or died).</summary>
    private void PayForAttendance(Patient patient)
    {
        if (patient.Triage is { } category && patient.Outcome != PatientOutcome.LeftWithoutBeingSeen)
        {
            Book(MoneyCategory.Attendances, AttendanceTariff[category]);
        }
    }

    /// <summary>What a dose costs: list price with a pharmacy, more without.</summary>
    public long MedicineCost(int costPerDose) =>
        HasPharmacy ? costPerDose : (long)Math.Round(costPerDose * NoPharmacyMedicineMarkup);

    private void PayForAdmission(Patient patient)
    {
        var nights = patient.AdmittedAt is { } admitted ? (int)Math.Floor((Now - admitted).TotalDays) : 0;
        Book(MoneyCategory.Admissions, AdmissionTariff + (nights * BedDayTariff) + CriticalCarePayment(patient));
    }
}
