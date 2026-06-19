import { ConfigProvider } from 'antd';
import { AppLayout } from './components/Layout/AppLayout';

function App() {
  return (
    <ConfigProvider
      theme={{
        token: {
          colorPrimary: '#1976d2',
          fontSize: 18,
          sizeUnit: 5,
          borderRadius: 8,
        },
      }}
    >
      <AppLayout />
    </ConfigProvider>
  );
}

export default App;
