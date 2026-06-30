"use client";

import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis
} from "recharts";
import { breachData, riskCategories, trendData } from "@/lib/audits";

const colors = ["#0078d4", "#4f9fe5", "#55b7d9", "#16836a", "#c2413b"];

export function RiskHeatmap() {
  return (
    <div className="glass-card rounded-md p-4">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Risk Heatmap</h2>
      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-5">
        {riskCategories.map((item) => (
          <div
            key={item.name}
            className="rounded-md border border-white/60 p-3 shadow-sm"
            style={{ backgroundColor: `rgba(0, 120, 212, ${Math.max(item.risk / 180, 0.12)})` }}
          >
            <div className="text-sm font-semibold text-ink">{item.name}</div>
            <div className="mt-6 text-2xl font-semibold">{item.risk}</div>
            <div className="text-xs text-graphite">{item.audits} audits</div>
          </div>
        ))}
      </div>
    </div>
  );
}

export function TrendChart() {
  return (
    <div className="glass-card h-80 rounded-md p-4">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Audit Outcomes</h2>
      <ResponsiveContainer width="100%" height="88%">
        <AreaChart data={trendData} margin={{ left: -20, right: 8, top: 20 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(126, 164, 214, 0.36)" />
          <XAxis dataKey="week" tickLine={false} axisLine={false} />
          <YAxis tickLine={false} axisLine={false} />
          <Tooltip />
          <Area type="monotone" dataKey="passed" stackId="1" stroke="#16836a" fill="#16836a" fillOpacity={0.18} />
          <Area type="monotone" dataKey="flagged" stackId="1" stroke="#0078d4" fill="#0078d4" fillOpacity={0.2} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

export function HoursSavedChart() {
  return (
    <div className="glass-card h-80 rounded-md p-4">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Hours Saved</h2>
      <ResponsiveContainer width="100%" height="88%">
        <BarChart data={trendData} margin={{ left: -20, right: 8, top: 20 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(126, 164, 214, 0.36)" />
          <XAxis dataKey="week" tickLine={false} axisLine={false} />
          <YAxis tickLine={false} axisLine={false} />
          <Tooltip />
          <Bar dataKey="hours" radius={[4, 4, 0, 0]} fill="#0078d4" />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

export function BreachChart() {
  return (
    <div className="glass-card h-80 rounded-md p-4">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Recurring Breaches</h2>
      <ResponsiveContainer width="100%" height="88%">
        <PieChart>
          <Pie data={breachData} dataKey="value" nameKey="name" innerRadius={58} outerRadius={96} paddingAngle={3}>
            {breachData.map((entry, index) => (
              <Cell key={entry.name} fill={colors[index % colors.length]} />
            ))}
          </Pie>
          <Tooltip />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
