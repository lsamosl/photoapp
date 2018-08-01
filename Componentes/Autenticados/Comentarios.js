import React, { Component } from 'react';
import { View, Text, Button } from 'react-native';

export default class Comentarios extends Component {
  static navigationOptions = {
    tabBarVisible: false,
  };

  render() {
    const { navigation } = this.props;
    console.log(this.props);
    return (
      <View style={styles.container}>
        <Text> Comentarios </Text>
        <Button title="Autor" onPress={() => { navigation.navigate('Autor'); }} />
      </View>
    );
  }
}

const styles = ({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#2c3e50',
  },
});
