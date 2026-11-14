import { Center, Loader } from '@mantine/core';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useMe } from './lib';
import { TabBar } from './components/Layout';
import { LoginPage } from './pages/Login';
import { RequestsPage } from './pages/Requests';
import { NewRequestPage } from './pages/NewRequest';
import { RequestDetailPage } from './pages/RequestDetail';
import { LedgerPage } from './pages/Ledger';
import { DealPage } from './pages/Deal';
import { CirclesPage } from './pages/Circles';
import { CircleDetailPage } from './pages/CircleDetail';
import { PersonPage } from './pages/Person';
import { MyProfilePage } from './pages/MyProfile';
import { SettingsPage } from './pages/Settings';

export function App() {
  const me = useMe();

  if (me.isLoading) {
    return (
      <Center h="100dvh">
        <Loader color="amber" />
      </Center>
    );
  }
  if (me.isError || !me.data) return <LoginPage />;

  return (
    <>
      <Routes>
        <Route path="/" element={<Navigate to="/requests" replace />} />
        <Route path="/requests" element={<RequestsPage />} />
        <Route path="/requests/new" element={<NewRequestPage />} />
        <Route path="/requests/:id" element={<RequestDetailPage />} />
        <Route path="/owe" element={<LedgerPage side="iOwe" />} />
        <Route path="/owed" element={<LedgerPage side="owedToMe" />} />
        <Route path="/deals/:id" element={<DealPage />} />
        <Route path="/circles" element={<CirclesPage />} />
        <Route path="/circles/:id" element={<CircleDetailPage />} />
        <Route path="/people/:id" element={<PersonPage />} />
        <Route path="/me" element={<MyProfilePage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/requests" replace />} />
      </Routes>
      <TabBar />
    </>
  );
}
