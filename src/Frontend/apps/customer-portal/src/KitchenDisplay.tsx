import { useEffect, useState, type ReactNode } from "react";
import { Alert, Button, Space, Typography } from "antd";
import { WorkspaceFrame } from "@nexaconnect/layout";

/** Same portal/session and queue, with focused presentation only. */
export function KitchenDisplay({ actions, children }: { actions: ReactNode; children: ReactNode }) {
  const [fullscreen, setFullscreen] = useState(!!document.fullscreenElement);
  const [notice, setNotice] = useState<string>();
  useEffect(() => {
    const changed = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", changed);
    return () => document.removeEventListener("fullscreenchange", changed);
  }, []);
  const toggleFullscreen = async () => {
    setNotice(undefined);
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else if (document.documentElement.requestFullscreen) await document.documentElement.requestFullscreen();
      else setNotice("Fullscreen is unavailable. Continue in this window or use your browser's fullscreen control.");
    } catch {
      setNotice("Fullscreen could not be changed. Continue in this window or use your browser's fullscreen control.");
    }
  };
  return <div className="nexa-kitchen-display">
    <header className="nexa-topbar">
      <Typography.Text strong>NexaConnect / Kitchen</Typography.Text>
      <div className="nexa-topbar-actions"><Space wrap>
        <Button href="/#kitchen">Return to manager portal</Button>
        <Button onClick={() => void toggleFullscreen()}>{fullscreen ? "Exit fullscreen" : "Enter fullscreen"}</Button>
        {actions}
      </Space></div>
    </header>
    <main className="nexa-main">
      <WorkspaceFrame title="Kitchen display" description="Live preparation queue for your authorized branch and station.">
        {notice && <Alert role="status" type="info" message={notice} />}
        {children}
      </WorkspaceFrame>
    </main>
  </div>;
}
