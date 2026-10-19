using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The device action tools (Emgu.LiteRT.LM.Agents' DeviceActions.cs) and the times they read in the user's words
// (WhenParser); no model or native library.
[TestFixture]
public class UnitTestDeviceActions
{
    // A Monday afternoon.
    private static readonly DateTime Now = new DateTime(2026, 10, 12, 15, 0, 0);

    [Test]
    public void ParsesDurations()
    {
        (string Text, TimeSpan Expected)[] cases =
        {
            ("10 minutes", TimeSpan.FromMinutes(10)),
            ("1 hour 30 minutes", TimeSpan.FromMinutes(90)),
            ("1h30m", TimeSpan.FromMinutes(90)),
            ("90 s", TimeSpan.FromSeconds(90)),
            ("1.5 hours", TimeSpan.FromMinutes(90)),
            ("half an hour", TimeSpan.FromMinutes(30)),
            ("two and a half hours", TimeSpan.FromMinutes(150)),
            ("2 hours and a half", TimeSpan.FromMinutes(150)),
            ("an hour", TimeSpan.FromHours(1)),
            ("forty-five minutes", TimeSpan.FromMinutes(45)),
            ("5:00", TimeSpan.FromMinutes(5)),
            ("1:30:00", TimeSpan.FromMinutes(90)),
            ("PT10M", TimeSpan.FromMinutes(10)),
            ("10", TimeSpan.FromMinutes(10)),
            ("a 3 minute timer", TimeSpan.FromMinutes(3)),
        };
        foreach (var c in cases)
        {
            Assert.That(WhenParser.TryParseDuration(c.Text, out TimeSpan duration), Is.True, c.Text);
            Assert.That(duration, Is.EqualTo(c.Expected), c.Text);
        }
        foreach (string bad in new[] { "", "bake the bread", "minutes", "10 apples" })
            Assert.That(WhenParser.TryParseDuration(bad, out _), Is.False, bad);
    }

    [Test]
    public void ParsesDatesAndTimes()
    {
        (string Text, DateTime Expected, bool HasTime)[] cases =
        {
            ("tomorrow at 3pm", new DateTime(2026, 10, 13, 15, 0, 0), true),
            ("today 15:30", new DateTime(2026, 10, 12, 15, 30, 0), true),
            ("at 4", new DateTime(2026, 10, 12, 16, 0, 0), true),
            ("9", new DateTime(2026, 10, 12, 21, 0, 0), true),
            ("7:30 am", new DateTime(2026, 10, 13, 7, 30, 0), true),
            ("7:30 a.m.", new DateTime(2026, 10, 13, 7, 30, 0), true),
            ("noon", new DateTime(2026, 10, 13, 12, 0, 0), true),
            ("midnight", new DateTime(2026, 10, 13, 0, 0, 0), true),
            ("tonight", new DateTime(2026, 10, 12, 20, 0, 0), true),
            ("tonight at 8", new DateTime(2026, 10, 12, 20, 0, 0), true),
            ("tomorrow morning", new DateTime(2026, 10, 13, 9, 0, 0), true),
            ("tomorrow at 7 in the evening", new DateTime(2026, 10, 13, 19, 0, 0), true),
            ("Friday", new DateTime(2026, 10, 16), false),
            ("Friday at 2", new DateTime(2026, 10, 16, 14, 0, 0), true),
            ("next Monday at 9", new DateTime(2026, 10, 19, 9, 0, 0), true),
            ("monday", new DateTime(2026, 10, 19), false),
            ("this Monday", new DateTime(2026, 10, 12), false),
            ("October 21", new DateTime(2026, 10, 21), false),
            ("October 21st at 9:30", new DateTime(2026, 10, 21, 9, 30, 0), true),
            ("21 Oct 2027 at noon", new DateTime(2027, 10, 21, 12, 0, 0), true),
            ("on the 3rd of March", new DateTime(2027, 3, 3), false),
            ("March 3", new DateTime(2027, 3, 3), false),
            ("in 2 hours", new DateTime(2026, 10, 12, 17, 0, 0), true),
            ("in 1 hour 30 minutes", new DateTime(2026, 10, 12, 16, 30, 0), true),
            ("in 3 days", new DateTime(2026, 10, 15), false),
            ("the day after tomorrow at 10am", new DateTime(2026, 10, 14, 10, 0, 0), true),
            ("2026-10-21T15:00", new DateTime(2026, 10, 21, 15, 0, 0), true),
            ("2026-10-21", new DateTime(2026, 10, 21), false),
        };
        foreach (var c in cases)
        {
            Assert.That(WhenParser.TryParseDateTime(c.Text, Now, out DateTime value, out bool hasTime), Is.True, c.Text);
            TestContext.Out.WriteLine("\"{0}\" -> {1}", c.Text, WhenParser.Describe(value, hasTime));
            Assert.That(value, Is.EqualTo(c.Expected), c.Text);
            Assert.That(hasTime, Is.EqualTo(c.HasTime), c.Text);
        }
        foreach (string bad in new[] { "", "after lunch", "tomorrow 25:00", "February 30", "soon" })
            Assert.That(WhenParser.TryParseDateTime(bad, Now, out _, out _), Is.False, bad);
    }

    [Test]
    public async Task ToolsPassTheRequestOnAndReportTheOutcome()
    {
        List<DeviceActionRequest> requests = new List<DeviceActionRequest>();
        bool confirm = true;
        Dictionary<string, AIFunction> tools = new Dictionary<string, AIFunction>();
        foreach (AIFunction tool in AgentTools.DeviceActions(DeviceActionKinds.All, (request, token) =>
            {
                requests.Add(request);
                return Task.FromResult(confirm ? AgentTools.DeviceActionDone(request.Summary) : AgentTools.DeviceActionCancelled);
            }, () => Now))
            tools[tool.Name] = tool;
        Assert.That(tools.Keys, Is.EquivalentTo(new[] { "set_timer", "set_alarm", "add_calendar_event", "add_reminder", "open_directions" }));

        async Task<string> Call(string name, params (string, object)[] arguments)
        {
            AIFunctionArguments args = new AIFunctionArguments();
            foreach (var (key, value) in arguments)
                args[key] = value;
            return (await tools[name].InvokeAsync(args))?.ToString();
        }

        string result = await Call("set_timer", ("duration", "10 minutes"), ("label", "Pasta"));
        Assert.That(result, Does.StartWith("Done: a timer for 10 minutes called \"Pasta\""));
        Assert.That(((TimerRequest)requests[^1]).Duration, Is.EqualTo(TimeSpan.FromMinutes(10)));

        result = await Call("add_calendar_event", ("title", "Dentist"), ("when", "Friday at 2"), ("duration", "45 minutes"), ("location", "Main St"));
        CalendarEventRequest calendarEvent = (CalendarEventRequest)requests[^1];
        Assert.That(calendarEvent.Start, Is.EqualTo(new DateTime(2026, 10, 16, 14, 0, 0)));
        Assert.That(calendarEvent.End, Is.EqualTo(new DateTime(2026, 10, 16, 14, 45, 0)));
        Assert.That(calendarEvent.AllDay, Is.False);
        Assert.That(result, Does.Contain("Friday 16 October 2026 at 14:00"));

        await Call("add_calendar_event", ("title", "Holiday"), ("when", "October 21"));
        calendarEvent = (CalendarEventRequest)requests[^1];
        Assert.That(calendarEvent.AllDay, Is.True);
        Assert.That(calendarEvent.End, Is.EqualTo(new DateTime(2026, 10, 22)));

        await Call("add_reminder", ("title", "Call mum"));
        Assert.That(((ReminderRequest)requests[^1]).Due, Is.Null);

        await Call("open_directions", ("destination", "Stanley Park"), ("mode", "walking"));
        Assert.That(((DirectionsRequest)requests[^1]).Mode, Is.EqualTo(TravelMode.Walking));

        // Times it can't read, or that have passed, go back to the model without asking the user.
        int asked = requests.Count;
        Assert.That(await Call("set_alarm", ("time", "after lunch")), Does.StartWith("I couldn't read the time"));
        Assert.That(await Call("set_alarm", ("time", "today at 9am")), Does.StartWith("That time has passed"));
        Assert.That(await Call("set_timer", ("duration", "3 days")), Does.StartWith("I couldn't read"));
        Assert.That(requests.Count, Is.EqualTo(asked));

        confirm = false;
        Assert.That(await Call("set_alarm", ("time", "7am")), Does.StartWith("The user cancelled"));
        Assert.That(((AlarmRequest)requests[^1]).Time, Is.EqualTo(new DateTime(2026, 10, 13, 7, 0, 0)));
    }
}
