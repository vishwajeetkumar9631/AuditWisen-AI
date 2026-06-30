"use client";

import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import type { Audit } from "@/lib/audits";

export type AuditHubPayload = Partial<Audit> & {
  id: string;
  message?: string;
};

export function useSignalR(hubUrl: string) {
  const [liveUpdate, setLiveUpdate] = useState<AuditHubPayload | null>(null);
  const [connectionState, setConnectionState] = useState<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected
  );

  useEffect(() => {
    if (!hubUrl) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.onreconnecting(() => setConnectionState(signalR.HubConnectionState.Reconnecting));
    connection.onreconnected(() => setConnectionState(signalR.HubConnectionState.Connected));
    connection.onclose(() => setConnectionState(signalR.HubConnectionState.Disconnected));

    connection.on("ReceiveAuditResult", (auditPayload: AuditHubPayload) => {
      setLiveUpdate(auditPayload);
    });

    connection
      .start()
      .then(() => setConnectionState(connection.state))
      .catch(() => setConnectionState(signalR.HubConnectionState.Disconnected));

    return () => {
      connection.stop();
    };
  }, [hubUrl]);

  return { liveUpdate, connectionState };
}
